namespace Najm.Tests;

/// <summary>
/// Measures the managed bytes a warm operation allocates, with the settling protocol that makes
/// the answer stable.
/// </summary>
/// <remarks>
/// <para>
/// Two effects make a naive <see cref="GC.GetAllocatedBytesForCurrentThread"/> difference lie,
/// and both were found the hard way.
/// </para>
/// <para>
/// First, some allocations hide behind caches the collector is free to drop — reflection metadata
/// behind <see cref="Enum.IsDefined{TEnum}(TEnum)"/> is the case that bit us. A loop measured
/// without a prior collection can report zero for thousands of iterations and then report a real
/// number on the run where a collection happened to land, which presents as an unreproducible
/// flake rather than as the genuine per-frame allocation it is. Forcing the collection ourselves
/// puts that repopulation inside the measurement window deliberately.
/// </para>
/// <para>
/// Second, <see cref="GC.Collect()"/> charges the calling thread a small fixed amount — about 248
/// bytes — against the <em>next</em> measurement window, regardless of how long that window is.
/// So the collection cannot immediately precede the baseline. Settling iterations run between
/// them to absorb that charge and to repopulate whatever the collection dropped.
/// </para>
/// </remarks>
internal static class AllocationProbe
{
    /// <summary>Iterations run before the forced collection, to reach steady state.</summary>
    private const int WarmIterations = 8;

    /// <summary>
    /// Iterations run after the forced collection and before the baseline, absorbing both the
    /// collection's own charge and any cache repopulation it caused.
    /// </summary>
    private const int SettleIterations = 8;

    /// <summary>Measures the managed bytes <paramref name="body"/> allocates per warm iteration.</summary>
    /// <param name="iterations">The number of measured iterations; must be positive.</param>
    /// <param name="body">The operation to measure. It runs additional unmeasured times.</param>
    /// <returns>Total managed bytes allocated across the measured iterations.</returns>
    internal static long Measure(int iterations, Action body)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentNullException.ThrowIfNull(body);

        for (var index = 0; index < WarmIterations; index++)
        {
            body();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        for (var index = 0; index < SettleIterations; index++)
        {
            body();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < iterations; index++)
        {
            body();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Asserts that <paramref name="body"/> allocates nothing once warm.</summary>
    /// <param name="iterations">The number of measured iterations; must be positive.</param>
    /// <param name="body">The operation to measure.</param>
    /// <param name="what">
    /// A short description of the operation, used in the failure message so a regression names
    /// what started allocating.
    /// </param>
    internal static void AssertNoneAllocated(int iterations, Action body, string what)
    {
        var allocated = Measure(iterations, body);
        Assert.AreEqual(
            0L,
            allocated,
            $"{what} allocated {allocated} managed bytes across {iterations} warm iterations.");
    }
}
