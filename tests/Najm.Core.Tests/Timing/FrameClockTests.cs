namespace Najm.Core.Tests.Timing;

[TestClass]
public sealed class FrameClockTests
{
    [TestMethod]
    public void FixedClockDerivesEveryPostAdvanceTick()
    {
        var clock = new FrameClock(ClockPolicy.Fixed(60d));

        for (var expectedFrame = 0L; expectedFrame < 100_000L; expectedFrame++)
        {
            var time = clock.Advance();

            Assert.AreEqual(expectedFrame, time.Frame);
            Assert.AreEqual(1d / 60d, time.Dt);
            Assert.AreEqual(((double)expectedFrame + 1d) / 60d, time.Elapsed);
            Assert.IsTrue(time.IsFixedStep);
        }
    }

    [TestMethod]
    public void LiveClockClampsAndAccumulatesPostAdvanceTime()
    {
        var clock = new FrameClock(ClockPolicy.Live(maxDt: 0.1d));

        var first = clock.Advance(wallDt: 0.25d);
        var second = clock.Advance(wallDt: 0.05d);
        var third = clock.Advance(wallDt: 0d);

        AssertLiveTick(first, frame: 0L, dt: 0.1d, elapsed: 0.1d);
        AssertLiveTick(second, frame: 1L, dt: 0.05d, elapsed: 0.15d);
        AssertLiveTick(third, frame: 2L, dt: 0d, elapsed: 0.15d);
    }

    [TestMethod]
    public void LiveClockRejectsInvalidWallDeltaWithoutAdvancingState()
    {
        var clock = new FrameClock(ClockPolicy.Live(maxDt: 1d));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(-0.1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => clock.Advance(double.PositiveInfinity));

        var first = clock.Advance(0.25d);
        Assert.AreEqual(0L, first.Frame);
        Assert.AreEqual(0.25d, first.Elapsed);
    }

    [TestMethod]
    public void ClockModesFailLoudlyOnWrongAdvanceMethod()
    {
        var fixedClock = new FrameClock(ClockPolicy.Fixed(60d));
        var liveClock = new FrameClock(ClockPolicy.Live(0.1d));

        Assert.ThrowsExactly<InvalidOperationException>(() => fixedClock.Advance(0.01d));
        Assert.ThrowsExactly<InvalidOperationException>(() => liveClock.Advance());
    }

    [TestMethod]
    public void LiveElapsedOverflowDoesNotPartiallyAdvanceState()
    {
        var clock = new FrameClock(ClockPolicy.Live(double.MaxValue));
        var first = clock.Advance(double.MaxValue);

        Assert.AreEqual(double.MaxValue, first.Elapsed);
        Assert.ThrowsExactly<OverflowException>(() => clock.Advance(double.MaxValue));

        var second = clock.Advance(0d);
        Assert.AreEqual(1L, second.Frame);
        Assert.AreEqual(double.MaxValue, second.Elapsed);
    }

    [TestMethod]
    public void OneHundredThousandWarmClockAdvancesAllocateNoManagedMemory()
    {
        var fixedClock = new FrameClock(ClockPolicy.Fixed(60d));
        var liveClock = new FrameClock(ClockPolicy.Live(0.1d));
        var accumulator = fixedClock.Advance().Elapsed + liveClock.Advance(0.01d).Elapsed;

        var reading = AllocationProbe.AssertNoneAllocated(
            100_000,
            () =>
            {
                accumulator += fixedClock.Advance().Elapsed;
                accumulator += liveClock.Advance(0.01d).Elapsed;
            },
            "One hundred thousand warm clock advances");

        Assert.AreEqual(reading.Invocations + 1L, fixedClock.Advance().Frame);
        Assert.AreEqual(reading.Invocations + 1L, liveClock.Advance(0.01d).Frame);
        Assert.IsGreaterThan(0d, accumulator);
    }

    private static void AssertLiveTick(TimeInfo time, long frame, double dt, double elapsed)
    {
        Assert.AreEqual(frame, time.Frame);
        Assert.AreEqual(dt, time.Dt, 1e-15d);
        Assert.AreEqual(elapsed, time.Elapsed, 1e-15d);
        Assert.IsFalse(time.IsFixedStep);
    }
}
