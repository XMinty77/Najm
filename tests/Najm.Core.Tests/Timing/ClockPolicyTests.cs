namespace Najm.Core.Tests.Timing;

[TestClass]
public sealed class ClockPolicyTests
{
    [TestMethod]
    public void FixedAndLiveExposeOnlyTheirOwnParameters()
    {
        var fixedPolicy = ClockPolicy.Fixed(60d);
        var livePolicy = ClockPolicy.Live(0.1d);

        Assert.IsTrue(fixedPolicy.IsValid);
        Assert.IsTrue(fixedPolicy.IsFixedStep);
        Assert.AreEqual(60d, fixedPolicy.FramesPerSecond);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = fixedPolicy.MaxDt);

        Assert.IsTrue(livePolicy.IsValid);
        Assert.IsFalse(livePolicy.IsFixedStep);
        Assert.AreEqual(0.1d, livePolicy.MaxDt);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = livePolicy.FramesPerSecond);
    }

    [TestMethod]
    public void InvalidPolicyParametersFailAtConstruction()
    {
        foreach (var invalid in new[] { -1d, 0d, double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClockPolicy.Fixed(invalid));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClockPolicy.Live(invalid));
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClockPolicy.Fixed(double.Epsilon));
    }

    [TestMethod]
    public void DefaultPolicyIsExplicitlyInvalid()
    {
        ClockPolicy policy = default;

        Assert.IsFalse(policy.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = policy.IsFixedStep);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = policy.FramesPerSecond);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = policy.MaxDt);
        Assert.ThrowsExactly<ArgumentException>(() => new FrameClock(policy));
    }
}

