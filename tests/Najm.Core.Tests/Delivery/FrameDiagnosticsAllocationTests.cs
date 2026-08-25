namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The cost claims the frame-diagnostics family makes about itself, measured rather than asserted
/// in prose.
/// </summary>
/// <remarks>
/// <para>
/// The family is diagnostic and offline, and the register entry that introduced it says so. That
/// claim is worth nothing unmeasured: the whole point of a byte-identity check is that it is cheap
/// enough to leave in a loop, and the whole point of a reusable <see cref="FrameStats"/> is that
/// measuring a thousand-frame sequence does not allocate a thousand histograms. Both are pinned
/// here.
/// </para>
/// <para>
/// <see cref="AllocationProbe"/> supplies the protocol — warm, collect, settle, then measure inside
/// a window that notices whether another thread's collection landed in it and retries if one did.
/// This suite runs method-level parallel, so an unguarded window reads other threads' collection
/// side effects as this thread's allocations; that class documents the measurements behind the
/// protocol at length and is worth reading before changing anything here.
/// </para>
/// </remarks>
[TestClass]
public sealed class FrameDiagnosticsAllocationTests
{
    private const int Iterations = 64;

    /// <summary>
    /// The identity check allocates nothing, which is what makes it safe in a per-frame loop.
    /// </summary>
    /// <remarks>
    /// It is the most-used check in the project's history and the one most likely to end up inside
    /// something warm — a determinism harness comparing every frame against its previous run, say.
    /// Row-at-a-time <c>SequenceEqual</c> over the leases' own spans is what keeps it free; anything
    /// that copied a row, or built an enumerator over it, would show up here immediately.
    /// </remarks>
    [TestMethod]
    public void AByteIdentityCheckAllocatesNothing()
    {
        using var first = TestFrame.Uniform(64, 64, 30, 60, 90);
        using var second = TestFrame.Uniform(64, 64, 30, 60, 90);
        var identical = true;

        var reading = AllocationProbe.AssertNoneAllocated(
            Iterations,
            () => identical &= FrameComparison.AreIdentical(first, second),
            "FrameComparison.AreIdentical over a 64x64 frame");

        Assert.IsTrue(identical, "The frames really are identical, so the scan ran to completion.");
        Assert.IsGreaterThan(
            Iterations - 1,
            reading.Invocations,
            "The probe runs warm and settle iterations on top of the measured ones.");
    }

    /// <summary>The full difference report allocates nothing either, identical frames or not.</summary>
    /// <remarks>
    /// <see cref="FrameDifference"/> is a struct returned by value and the scan reads spans, so
    /// there is nothing to allocate. Measured on a differing pair as well as an identical one,
    /// because the identical path exits each row early and would hide a per-pixel allocation on the
    /// path that actually does the work.
    /// </remarks>
    [TestMethod]
    public void ADifferenceReportAllocatesNothingOnEitherPath()
    {
        using var reference = TestFrame.Uniform(64, 64, 30, 60, 90);
        using var identical = TestFrame.Uniform(64, 64, 30, 60, 90);
        using var differing = TestFrame.Uniform(64, 64, 30, 60, 90);
        for (var y = 0; y < 64; y++)
        {
            TestFrame.Set(differing, y % 64, y, 31, 60, 90);
        }

        var differingPixels = 0L;

        AllocationProbe.AssertNoneAllocated(
            Iterations,
            () => _ = FrameComparison.Between(identical, reference),
            "FrameComparison.Between over an identical 64x64 pair");

        AllocationProbe.AssertNoneAllocated(
            Iterations,
            () => differingPixels = FrameComparison.Between(differing, reference).DifferingPixels,
            "FrameComparison.Between over a differing 64x64 pair");

        Assert.AreEqual(64L, differingPixels, "The differing path really did visit every changed pixel.");
    }

    /// <summary>
    /// A reused <see cref="FrameStats"/> allocates nothing per frame, so measuring a sequence costs
    /// one set of histograms rather than one per frame.
    /// </summary>
    /// <remarks>
    /// This is the claim the type's own remarks make about reuse, and the reason
    /// <c>Measure</c> resets in place instead of returning a new instance. Note that
    /// <c>Enum.IsDefined</c> in the argument guard reads reflection metadata that a collection is
    /// free to drop; that is exactly the cache <see cref="AllocationProbe"/>'s settle-and-retry
    /// protocol exists to keep out of the window, so no exception is made for it here.
    /// </remarks>
    [TestMethod]
    public void ReMeasuringIntoTheSameInstanceAllocatesNothingPerFrame()
    {
        using var frame = TestFrame.Uniform(64, 64, 30, 60, 90);
        var stats = new FrameStats();

        AllocationProbe.AssertNoneAllocated(
            Iterations,
            () => stats.Measure(frame),
            "FrameStats.Measure into a reused instance over a 64x64 frame");

        Assert.AreEqual(4096L, stats.PixelCount);
    }

    /// <summary>
    /// A fresh measurement costs the same however large the frame is: histograms, not pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The register entry claims "a fixed amount independent of frame size". Two sizes sixteen times
    /// apart in area, measured the same way and compared to each other, is what that sentence means
    /// operationally — and it is a stronger statement than any absolute byte count, which would
    /// merely re-encode today's object layout into an assertion.
    /// </para>
    /// <para>
    /// Both readings are also asserted to be the histogram array and its object, not something that
    /// grows: seven 256-entry <c>int</c> histograms are 7168 bytes, so a per-iteration cost in that
    /// neighbourhood is the whole of it and a cost proportional to 262144 pixels would not be.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AFreshMeasurementCostsTheSameWhateverTheFrameSize()
    {
        using var small = TestFrame.Uniform(64, 64, 30, 60, 90);
        using var large = TestFrame.Uniform(512, 512, 30, 60, 90);

        var smallReading = AllocationProbe.Measure(Iterations, () => _ = FrameStats.Of(small));
        var largeReading = AllocationProbe.Measure(Iterations, () => _ = FrameStats.Of(large));

        Assert.AreEqual(
            smallReading.AllocatedBytes,
            largeReading.AllocatedBytes,
            $"A 512x512 frame is 64 times the pixels of a 64x64 one and must cost the same to " +
            $"measure. 64x64 read {smallReading.AllocatedBytes} bytes{smallReading.Describe()}; " +
            $"512x512 read {largeReading.AllocatedBytes} bytes{largeReading.Describe()}.");

        var perIteration = smallReading.AllocatedBytes / Iterations;
        Assert.IsLessThan(
            8192L,
            perIteration,
            $"One measurement is one FrameStats holding 7168 bytes of histograms; {perIteration} " +
            "bytes per iteration is more than that and means something else is allocating.");
    }
}
