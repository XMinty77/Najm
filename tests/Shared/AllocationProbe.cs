namespace Najm.Tests;

/// <summary>
/// Measures the managed bytes a warm operation allocates, with the protocol that makes the answer
/// stable under a parallel test run.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is itself exact: it reports the thread's
/// allocation context minus the unused tail of that context, so it needs no flushing and no
/// "precise" variant. Everything below is about what the code under test does, not about the
/// counter.
/// </para>
/// <para>
/// <strong>The effect.</strong> Some operations that are genuinely allocation-free in steady state
/// still allocate immediately after a collection, because they read a cache the collector is free
/// to drop. The confirmed case here is the reflection metadata behind
/// <see cref="Enum.IsDefined{TEnum}(TEnum)"/>, which every argument-validating enum guard in the
/// engine touches; repopulating it costs exactly 248 bytes, once per drop. Two consequences follow,
/// and they pull in opposite directions.
/// </para>
/// <para>
/// <strong>Consequence one: measuring without a collection under-reports.</strong> A loop measured
/// with no prior collection reads zero for thousands of iterations and then reads 248 on the run
/// where a background collection happens to land, so a real cost presents as an unreproducible
/// flake. Forcing a collection before the measurement puts the repopulation somewhere known.
/// </para>
/// <para>
/// <strong>Consequence two: the collection must not immediately precede the baseline.</strong>
/// Measured on this runtime, a window opened straight after <see cref="GC.Collect()"/> reports 248
/// bytes on <em>every single run</em> for a body that reads such a cache, because the first measured
/// iteration is the one that repopulates it. <see cref="SettleIterations"/> unmeasured iterations
/// between the collection and the baseline move that repopulation out of the window. (This is where
/// the "<c>GC.Collect</c> charges the calling thread 248 bytes" folklore came from. It does not: a
/// collection on a quiet thread charges zero. The 248 was always the cache.)
/// </para>
/// <para>
/// <strong>Why settling alone was not enough.</strong> The suite runs
/// <c>[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]</c>, so other test methods are
/// running — and calling <see cref="GC.Collect()"/> — on other threads throughout. A collection
/// they trigger can land <em>inside</em> an already-settled window, drop the cache, and have the
/// next iteration repay the 248. No amount of settling beforehand can prevent that; the window has
/// to notice it happened. Measured under a synthetic storm of concurrent collections, a settled
/// single-window protocol read 248 on 27 of 400 runs, which is the flake this class exists to kill.
/// </para>
/// <para>
/// That is reproducible in this suite. Setting <see cref="MaxAttempts"/> to 1 turns this class back
/// into the settled single-window protocol; running three copies of the whole suite at once on a
/// six-core machine then failed 3 runs out of 15, every one of them
/// <c>AWarmOfflineLoopAllocatesNoManagedBytesPerFrame</c>, reading 248, 248 and 80. The 80 is worth
/// noting: the number is whatever the dropped cache costs to rebuild, not a constant, which is the
/// last nail in the "<c>GC.Collect</c> charges 248" reading of it. With <see cref="MaxAttempts"/>
/// back at 4 the same stress passed 15 out of 15.
/// </para>
/// <para>
/// <strong>The protocol.</strong> Warm, force a collection, then repeat up to
/// <see cref="MaxAttempts"/> times: settle, snapshot <see cref="GC.CollectionCount(int)"/> for all
/// three generations, measure, and re-read the counts. A window whose counts did not move is
/// undisturbed and its reading is returned as-is. Otherwise the attempt is retried. Across 4000
/// storm trials this detector was exact in both directions: no undisturbed window ever read
/// non-zero, and every non-zero reading (248, or 496 for two drops) came from a disturbed one. Two
/// attempts sufficed under a storm of six threads collecting in a tight loop; the mean attempt count
/// was 1.01, so the retry is close to free when nothing is wrong.
/// </para>
/// <para>
/// <strong>Rejected alternatives.</strong> <c>GC.GetTotalAllocatedBytes(precise: true)</c> is
/// process-wide: under method-level parallelism it read up to 8.3 MB of other threads' traffic in a
/// window where this thread allocated nothing. <see cref="GC.TryStartNoGCRegion(long)"/> is also
/// process-wide and throws when two threads use it at once ("The NoGCRegion mode was already in
/// progress", "NoGCRegion mode must be set"), which a parallel suite guarantees.
/// </para>
/// <para>
/// <strong>Reading a non-zero result.</strong> The protocol never masks a real allocation: it
/// discards a window only on evidence that a collection ran inside it, and if every attempt is
/// disturbed it returns the smallest reading rather than giving up. Verified against controls that
/// allocate on purpose — 1000 escaping objects read exactly 24000 bytes, and a body allocating one
/// 16-byte array on every 250th of 1000 iterations read exactly 160. One caveat belongs to the JIT
/// rather than to this class: tiered compilation enables escape analysis late, so a body whose
/// allocation the optimiser can stack-allocate reads non-zero on early batches and zero once tier 1
/// arrives. Warm iterations before the first window keep that out of the measurement.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    /// <summary>Iterations run before the forced collection, to reach steady state.</summary>
    internal const int WarmIterations = 8;

    /// <summary>
    /// Iterations run after the forced collection and before each baseline, so that a cache the
    /// collection dropped is repopulated outside the measured window rather than inside it.
    /// </summary>
    internal const int SettleIterations = 8;

    /// <summary>
    /// How many windows to open before accepting a disturbed one. Two were enough under a storm of
    /// six threads collecting in a tight loop; four leaves margin without risking a long stall.
    /// </summary>
    internal const int MaxAttempts = 4;

    /// <summary>Forces the collection the protocol is built around, finalizers included.</summary>
    internal static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Measures the managed bytes <paramref name="body"/> allocates once warm.</summary>
    /// <param name="iterations">The number of iterations inside one measured window; must be positive.</param>
    /// <param name="body">The operation to measure. It runs additional unmeasured times.</param>
    /// <returns>
    /// The reading, including the total number of times <paramref name="body"/> ran so a caller with
    /// a side-effect counter can assert against the real total.
    /// </returns>
    internal static Reading Measure(int iterations, Action body)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentNullException.ThrowIfNull(body);

        var invocations = 0;
        for (var index = 0; index < WarmIterations; index++)
        {
            body();
        }

        invocations += WarmIterations;
        ForceCollection();

        var window = new Window();
        var best = long.MaxValue;
        var attempts = 0;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            attempts = attempt;
            for (var index = 0; index < SettleIterations; index++)
            {
                body();
            }

            invocations += SettleIterations;

            window.Open();
            for (var index = 0; index < iterations; index++)
            {
                body();
            }

            window.Close();
            invocations += iterations;

            if (!window.Disturbed)
            {
                return new Reading(window.AllocatedBytes, invocations, attempt, Disturbed: false);
            }

            best = Math.Min(best, window.AllocatedBytes);
        }

        return new Reading(best, invocations, attempts, Disturbed: true);
    }

    /// <summary>Asserts that <paramref name="body"/> allocates nothing once warm.</summary>
    /// <param name="iterations">The number of iterations inside one measured window; must be positive.</param>
    /// <param name="body">The operation to measure.</param>
    /// <param name="what">
    /// A short description of the operation, used in the failure message so a regression names what
    /// started allocating.
    /// </param>
    /// <returns>The reading, so a caller can assert its side-effect counters against
    /// <see cref="Reading.Invocations"/>.</returns>
    internal static Reading AssertNoneAllocated(int iterations, Action body, string what)
    {
        var reading = Measure(iterations, body);
        Assert.AreEqual(
            0L,
            reading.AllocatedBytes,
            $"{what} allocated {reading.AllocatedBytes} managed bytes across {iterations} warm " +
            $"iterations{reading.Describe()}.");
        return reading;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it produces an undisturbed sample, for loops the test
    /// cannot drive one iteration at a time — an offline render, say, where the measurement has to
    /// happen inside a run the renderer owns.
    /// </summary>
    /// <param name="attempt">
    /// Runs the whole loop once and returns what its <see cref="Window"/> saw. It is called at least
    /// once and at most <see cref="MaxAttempts"/> times.
    /// </param>
    /// <returns>
    /// The first undisturbed sample, or the smallest reading seen if every attempt was disturbed.
    /// </returns>
    internal static Sample MeasureUntilUndisturbed(Func<Sample> attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var best = new Sample(long.MaxValue, Disturbed: true);
        for (var index = 1; index <= MaxAttempts; index++)
        {
            var sample = attempt();
            if (!sample.Disturbed)
            {
                return sample;
            }

            if (sample.AllocatedBytes < best.AllocatedBytes)
            {
                best = sample;
            }
        }

        return best;
    }

    /// <summary>What one measured window saw.</summary>
    /// <param name="AllocatedBytes">Managed bytes allocated on this thread inside the window.</param>
    /// <param name="Disturbed">
    /// Whether a collection ran while the window was open. A disturbed window's reading is not
    /// trustworthy, because a collection can drop a cache that the code under test then repays for.
    /// </param>
    internal readonly record struct Sample(long AllocatedBytes, bool Disturbed);

    /// <summary>The outcome of a full <see cref="Measure"/> run.</summary>
    /// <param name="AllocatedBytes">Managed bytes allocated across one measured window.</param>
    /// <param name="Invocations">
    /// How many times the body ran in total — warm, settle, and measured iterations across every
    /// attempt. A test whose body bumps a counter should assert against this rather than against a
    /// hard-coded total, which the retry would make wrong.
    /// </param>
    /// <param name="Attempts">How many windows were opened.</param>
    /// <param name="Disturbed">Whether even the last window was disturbed by a collection.</param>
    internal readonly record struct Reading(
        long AllocatedBytes,
        int Invocations,
        int Attempts,
        bool Disturbed)
    {
        /// <summary>Renders the attempt history for a failure message.</summary>
        internal string Describe() =>
            Disturbed
                ? $" (every one of {Attempts} windows was disturbed by a collection, so this is the "
                    + "smallest reading rather than a clean one)"
                : Attempts == 1 ? string.Empty : $" (clean on attempt {Attempts})";
    }

    /// <summary>
    /// One measurement window, for loops driven by something other than the test body. Create it
    /// before the loop starts so its own allocation lands outside the window.
    /// </summary>
    internal sealed class Window
    {
        private long baseline;
        private int gen0;
        private int gen1;
        private int gen2;

        /// <summary>Managed bytes allocated on this thread between <see cref="Open"/> and <see cref="Close"/>.</summary>
        internal long AllocatedBytes { get; private set; } = -1L;

        /// <summary>Whether a collection ran while the window was open.</summary>
        internal bool Disturbed { get; private set; } = true;

        /// <summary>Whether <see cref="Close"/> has run since the last <see cref="Open"/>.</summary>
        internal bool IsClosed { get; private set; }

        /// <summary>The window as a <see cref="Sample"/>, for <see cref="MeasureUntilUndisturbed"/>.</summary>
        internal Sample ToSample() =>
            IsClosed
                ? new Sample(AllocatedBytes, Disturbed)
                : throw new InvalidOperationException("The measurement window was never closed.");

        /// <summary>Takes the baseline. Must be the last thing before the measured iterations.</summary>
        internal void Open()
        {
            IsClosed = false;
            gen0 = GC.CollectionCount(0);
            gen1 = GC.CollectionCount(1);
            gen2 = GC.CollectionCount(2);
            baseline = GC.GetAllocatedBytesForCurrentThread();
        }

        /// <summary>Takes the final reading. Must be the first thing after the measured iterations.</summary>
        internal void Close()
        {
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - baseline;
            Disturbed = GC.CollectionCount(0) != gen0
                || GC.CollectionCount(1) != gen1
                || GC.CollectionCount(2) != gen2;
            IsClosed = true;
        }
    }
}
