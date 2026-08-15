namespace Najm.Core.Tests.Timing;

[TestClass]
public sealed class FixedStepTimingTests
{
    [TestMethod]
    public void TickZeroUsesPostAdvanceConvention()
    {
        var time = FixedStepTiming.Tick(frame: 0L, framesPerSecond: 60d);

        Assert.AreEqual(0L, time.Frame);
        Assert.AreEqual(1d / 60d, time.Dt);
        Assert.AreEqual(1d / 60d, time.Elapsed);
        Assert.IsTrue(time.IsFixedStep);
    }

    [TestMethod]
    public void TickKIsDerivedDirectlyAtLargeFrameIndices()
    {
        const long frame = 9_000_000_000_000_000L;
        const double fps = 59.94d;

        var time = FixedStepTiming.Tick(frame, fps);

        Assert.AreEqual(frame, time.Frame);
        Assert.AreEqual(1d / fps, time.Dt);
        Assert.AreEqual(((double)frame + 1d) / fps, time.Elapsed);
    }

    [TestMethod]
    public void StillTickCountFollowsCeilingConventionIncludingZero()
    {
        Assert.AreEqual(0L, FixedStepTiming.TicksForStill(at: 0d, framesPerSecond: 60d));
        Assert.AreEqual(1L, FixedStepTiming.TicksForStill(at: 1d / 60d, framesPerSecond: 60d));
        Assert.AreEqual(30L, FixedStepTiming.TicksForStill(at: 0.5d, framesPerSecond: 60d));
        Assert.AreEqual(31L, FixedStepTiming.TicksForStill(at: 0.50001d, framesPerSecond: 60d));
        Assert.AreEqual(1L, FixedStepTiming.TicksForStill(at: double.Epsilon, framesPerSecond: 1d));
    }

    [TestMethod]
    public void TickElapsedRoundTripsAtRepresentativeFrameBoundaries()
    {
        foreach (var fps in new[] { 23.976d, 24d, 30d, 59.94d, 60d, 120d })
        {
            foreach (var frame in new[] { 0L, 1L, 2L, 29L, 30L, 31L, 1_000L, 1_000_000L })
            {
                var boundary = FixedStepTiming.Tick(frame, fps).Elapsed;

                Assert.AreEqual(
                    frame + 1L,
                    FixedStepTiming.TicksForStill(boundary, fps),
                    $"Round-trip failed for frame {frame} at {fps} fps.");
            }
        }
    }

    [TestMethod]
    public void AdjacentRepresentableTimesStayOnTheirCorrectSideOfBoundary()
    {
        foreach (var fps in new[] { 23.976d, 24d, 30d, 59.94d, 60d, 120d })
        {
            foreach (var frame in new[] { 0L, 1L, 30L, 1_000L, 1_000_000L })
            {
                var boundary = FixedStepTiming.Tick(frame, fps).Elapsed;
                var immediatelyBelow = Math.BitDecrement(boundary);
                var immediatelyAbove = Math.BitIncrement(boundary);

                Assert.AreEqual(
                    frame + 1L,
                    FixedStepTiming.TicksForStill(immediatelyBelow, fps),
                    $"BitDecrement crossed the frame boundary for frame {frame} at {fps} fps.");
                Assert.AreEqual(
                    frame + 2L,
                    FixedStepTiming.TicksForStill(immediatelyAbove, fps),
                    $"BitIncrement was swallowed for frame {frame} at {fps} fps.");
            }
        }
    }

    [TestMethod]
    public void BoundaryCorrectionAllocatesNoManagedMemory()
    {
        const double fps = 60d;
        var accumulator = FixedStepTiming.TicksForStill(FixedStepTiming.Tick(30L, fps).Elapsed, fps);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var iteration = 0; iteration < 100_000; iteration++)
        {
            var frame = iteration % 1_000;
            var boundary = FixedStepTiming.Tick(frame, fps).Elapsed;
            accumulator += FixedStepTiming.TicksForStill(boundary, fps);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.IsGreaterThan(0L, accumulator);
    }

    [TestMethod]
    public void FixedHelpersGuardInvalidAndOverflowingRequests()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FixedStepTiming.Tick(-1L, 60d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FixedStepTiming.Tick(0L, 0d));
        Assert.ThrowsExactly<OverflowException>(() => FixedStepTiming.Tick(long.MaxValue, 1e-300d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FixedStepTiming.TicksForStill(-1d, 60d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FixedStepTiming.TicksForStill(double.NaN, 60d));
        Assert.ThrowsExactly<OverflowException>(() => FixedStepTiming.TicksForStill(double.MaxValue, 60d));
        Assert.ThrowsExactly<OverflowException>(() =>
            FixedStepTiming.TicksForStill(9_223_372_036_854_775_808d, 1d));
    }
}
