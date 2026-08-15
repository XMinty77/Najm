namespace Najm.Core.Tests.Timing;

[TestClass]
public sealed class TimeInfoTests
{
    [TestMethod]
    public void ConstructedValueExposesImmutableTickData()
    {
        var time = new TimeInfo(elapsed: 1.5d, dt: 0.25d, frame: 5L, isFixedStep: false);

        Assert.IsTrue(time.IsValid);
        Assert.AreEqual(1.5d, time.Elapsed);
        Assert.AreEqual(0.25d, time.Dt);
        Assert.AreEqual(5L, time.Frame);
        Assert.IsFalse(time.IsFixedStep);
    }

    [TestMethod]
    public void InvalidTimeDataFailsAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(-1d, 0d, 0L, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(double.NaN, 0d, 0L, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(1d, -1d, 0L, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(1d, double.PositiveInfinity, 0L, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(0.5d, 1d, 0L, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimeInfo(1d, 1d, -1L, false));
    }

    [TestMethod]
    public void DefaultTimeIsExplicitlyInvalid()
    {
        TimeInfo time = default;

        Assert.IsFalse(time.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = time.Elapsed);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = time.Dt);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = time.Frame);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = time.IsFixedStep);
    }
}

