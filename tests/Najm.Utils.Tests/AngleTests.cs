using Najm.Utils;

namespace Najm.Utils.Tests;

[TestClass]
public sealed class AngleTests
{
    [TestMethod]
    public void DegreeAndRadianFactoriesAgree()
    {
        var fromDegrees = Angle.Deg(180d);
        var fromRadians = Angle.Rad(Math.PI);

        Assert.AreEqual(Math.PI, fromDegrees.Radians, 1e-14d);
        Assert.AreEqual(180d, fromRadians.Degrees, 1e-12d);
        Assert.AreEqual(fromRadians, fromDegrees);
    }

    [TestMethod]
    public void DefaultAngleIsValidZero()
    {
        Angle angle = default;

        Assert.AreEqual(Angle.Zero, angle);
        Assert.AreEqual(0d, angle.Radians);
    }

    [TestMethod]
    public void FactoriesDoNotNormalizeTurns()
    {
        var angle = Angle.Deg(-450d);

        Assert.AreEqual(-450d, angle.Degrees, 1e-12d);
    }

    [TestMethod]
    public void ArithmeticPreservesAngleSemantics()
    {
        var sum = Angle.QuarterTurn + Angle.Deg(30d);
        var difference = Angle.HalfTurn - Angle.QuarterTurn;
        var scaled = 2d * Angle.QuarterTurn;
        var divided = Angle.FullTurn / 4d;

        Assert.AreEqual(120d, sum.Degrees, 1e-12d);
        Assert.AreEqual(Angle.QuarterTurn, difference);
        Assert.AreEqual(Angle.HalfTurn, scaled);
        Assert.AreEqual(Angle.QuarterTurn, divided);
        Assert.AreEqual(4d, Angle.FullTurn / Angle.QuarterTurn, 1e-14d);
        Assert.AreEqual(Angle.Deg(-90d), -Angle.QuarterTurn);
    }

    [TestMethod]
    public void OrderingUsesStoredRadians()
    {
        Assert.IsTrue(Angle.Deg(-1d) < Angle.Zero);
        Assert.IsTrue(Angle.FullTurn > Angle.HalfTurn);
        Assert.IsTrue(Angle.QuarterTurn <= Angle.Deg(90d));
        Assert.IsTrue(Angle.HalfTurn >= Angle.QuarterTurn);
    }

    [TestMethod]
    public void InvalidNumericStateFailsAtBoundary()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Angle.Rad(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Angle.Deg(double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Angle.Zero * double.NaN);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Angle.HalfTurn / 0d);
        Assert.ThrowsExactly<DivideByZeroException>(() => _ = Angle.HalfTurn / Angle.Zero);
    }
}

