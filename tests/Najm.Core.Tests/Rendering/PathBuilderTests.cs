using System.Numerics;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class PathBuilderTests
{
    [TestMethod]
    public void Commands_ExposeEveryTierOneCurveVerbInOrder()
    {
        var path = new PathBuilder(FillRule.EvenOdd, initialCapacity: 5)
            .MoveTo(1f, 2f)
            .LineTo(3f, 4f)
            .QuadTo(5f, 6f, 7f, 8f)
            .CubicTo(9f, 10f, 11f, 12f, 13f, 14f)
            .Close();

        var commands = path.Commands;
        Assert.AreEqual(5, commands.Length);
        Assert.AreEqual(PathVerb.Move, commands[0].Verb);
        Assert.AreEqual(new Vector2(1f, 2f), commands[0].Point1);
        Assert.AreEqual(PathVerb.Line, commands[1].Verb);
        Assert.AreEqual(PathVerb.Quadratic, commands[2].Verb);
        Assert.AreEqual(new Vector2(5f, 6f), commands[2].Point1);
        Assert.AreEqual(new Vector2(7f, 8f), commands[2].Point2);
        Assert.AreEqual(PathVerb.Cubic, commands[3].Verb);
        Assert.AreEqual(new Vector2(13f, 14f), commands[3].Point3);
        Assert.AreEqual(PathVerb.Close, commands[4].Verb);
        Assert.AreEqual(FillRule.EvenOdd, path.FillRule);
    }

    [TestMethod]
    public void Reset_ClearsGeometryRetainsFillRuleAndAllowsReuse()
    {
        var path = new PathBuilder(FillRule.EvenOdd).MoveTo(0f, 0f).LineTo(1f, 1f);

        path.Reset();

        Assert.AreEqual(0, path.Count);
        Assert.AreEqual(0, path.Commands.Length);
        Assert.AreEqual(FillRule.EvenOdd, path.FillRule);
        Assert.ThrowsExactly<InvalidOperationException>(() => path.LineTo(1f, 1f));

        path.MoveTo(2f, 3f).Close();
        Assert.AreEqual(2, path.Count);
    }

    [TestMethod]
    public void Coordinates_MustBeFinite()
    {
        var path = new PathBuilder();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => path.MoveTo(float.NaN, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => path.MoveTo(0f, float.PositiveInfinity));
    }
}
