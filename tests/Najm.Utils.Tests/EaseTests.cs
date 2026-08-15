using Najm.Utils;

namespace Najm.Utils.Tests;

[TestClass]
public sealed class EaseTests
{
    [TestMethod]
    public void BuiltInsMapEndpointsExactly()
    {
        foreach (var timing in AllBuiltIns())
        {
            Assert.AreEqual(0f, timing.Evaluate(0f));
            Assert.AreEqual(1f, timing.Evaluate(1f));
        }
    }

    [TestMethod]
    public void QuadraticCurvesMatchReferenceSamples()
    {
        Assert.AreEqual(0.0625f, Ease.InQuad.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.4375f, Ease.OutQuad.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.125f, Ease.InOutQuad.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.5f, Ease.InOutQuad.Evaluate(0.5f), 1e-7f);
        Assert.AreEqual(0.875f, Ease.InOutQuad.Evaluate(0.75f), 1e-7f);
    }

    [TestMethod]
    public void CubicCurvesMatchReferenceSamples()
    {
        Assert.AreEqual(0.015625f, Ease.InCubic.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.578125f, Ease.OutCubic.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.0625f, Ease.InOutCubic.Evaluate(0.25f), 1e-7f);
        Assert.AreEqual(0.5f, Ease.InOutCubic.Evaluate(0.5f), 1e-7f);
        Assert.AreEqual(0.9375f, Ease.InOutCubic.Evaluate(0.75f), 1e-7f);
    }

    [TestMethod]
    public void EvaluationDoesNotImplicitlyClampFiniteProgress()
    {
        Assert.AreEqual(-0.25f, Ease.Linear.Evaluate(-0.25f));
        Assert.AreEqual(1.25f, Ease.Linear.Evaluate(1.25f));
    }

    [TestMethod]
    public void NonFiniteProgressFailsLoudly()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Ease.Linear.Evaluate(float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Ease.OutCubic.Evaluate(float.PositiveInfinity));
    }

    [TestMethod]
    public void InterfaceSupportsCustomTimingFunctions()
    {
        ITimingFunction timing = new HalfSpeedTiming();

        Assert.AreEqual(0.25f, timing.Evaluate(0.5f));
    }

    [TestMethod]
    public void ConcreteBuiltInEvaluationAllocatesNoManagedMemory()
    {
        var timing = Ease.InOutCubic;
        var accumulator = timing.Evaluate(0.25f);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            accumulator += timing.Evaluate((index % 1_000) / 1_000f);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.IsGreaterThan(0f, accumulator);
    }

    private static TimingFunction[] AllBuiltIns() =>
    [
        Ease.Linear,
        Ease.InQuad,
        Ease.OutQuad,
        Ease.InOutQuad,
        Ease.InCubic,
        Ease.OutCubic,
        Ease.InOutCubic,
    ];

    private readonly struct HalfSpeedTiming : ITimingFunction
    {
        public float Evaluate(float progress) => progress / 2f;
    }
}
