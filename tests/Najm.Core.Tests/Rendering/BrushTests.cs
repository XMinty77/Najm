using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class BrushTests
{
    [TestMethod]
    public void Default_IsATransparentSolidBrush()
    {
        var brush = default(Brush);

        Assert.AreEqual(BrushKind.Solid, brush.Kind);
        Assert.AreEqual(default, brush.Color);
        Assert.AreEqual(SpreadMode.Clamp, brush.Spread);
        Assert.AreEqual(0, brush.Stops.Length);
        Assert.IsNull(brush.Image);
        Assert.AreEqual(Brush.Solid(Color.Transparent), brush);
        Assert.AreEqual(Brush.Solid(Color.Transparent).GetHashCode(), brush.GetHashCode());
    }

    [TestMethod]
    public void IndependentlyConstructedGradients_AreEqualAndHashEqual()
    {
        var first = Brush.Linear(
            new Vector2(0f, 0f),
            new Vector2(10f, 4f),
            [
                new GradientStop(0f, Color.Srgb(1f, 0f, 0f)),
                new GradientStop(0.25f, Color.Srgb(0f, 1f, 0f, 0.5f)),
                new GradientStop(1f, Color.Srgb(0f, 0f, 1f)),
            ]);
        var second = Brush.Linear(
            new Vector2(0f, 0f),
            new Vector2(10f, 4f),
            [
                new GradientStop(0f, Color.Srgb(1f, 0f, 0f)),
                new GradientStop(0.25f, Color.Srgb(0f, 1f, 0f, 0.5f)),
                new GradientStop(1f, Color.Srgb(0f, 0f, 1f)),
            ]);

        Assert.AreEqual(first, second, "Gradients must compare by stop contents, not by array reference.");
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(first == second);
        Assert.IsFalse(first != second);
    }

    [TestMethod]
    public void EqualBrushValues_KeyOneDictionaryEntry()
    {
        var cache = new Dictionary<Brush, int>
        {
            [Brush.Radial(new Vector2(1f, 2f), 3f, Ramp())] = 1,
        };

        cache[Brush.Radial(new Vector2(1f, 2f), 3f, Ramp())] = 2;

        Assert.HasCount(1, cache, "A value-keyed shader cache must hit on an equal brush.");
        Assert.AreEqual(2, cache[Brush.Radial(new Vector2(1f, 2f), 3f, Ramp())]);
    }

    [TestMethod]
    public void BrushesDifferingInAnyDescriptorField_AreNotEqual()
    {
        var baseline = Brush.Linear(new Vector2(0f, 0f), new Vector2(1f, 0f), Ramp());

        Assert.AreNotEqual(baseline, Brush.Linear(new Vector2(0f, 0.5f), new Vector2(1f, 0f), Ramp()));
        Assert.AreNotEqual(baseline, Brush.Linear(new Vector2(0f, 0f), new Vector2(2f, 0f), Ramp()));
        Assert.AreNotEqual(
            baseline,
            Brush.Linear(new Vector2(0f, 0f), new Vector2(1f, 0f), Ramp(), SpreadMode.Mirror));
        Assert.AreNotEqual(baseline, Brush.Radial(new Vector2(0f, 0f), 1f, Ramp()));
        Assert.AreNotEqual(
            baseline,
            Brush.Linear(
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                [
                    new GradientStop(0f, Color.Black),
                    new GradientStop(0.5f, Color.White),
                    new GradientStop(1f, Color.White),
                ]));
        Assert.AreNotEqual(baseline, Brush.Solid(Color.White));
    }

    [TestMethod]
    public void MutatingTheCallerArray_DoesNotChangeTheBrush()
    {
        var stops = Ramp();
        var brush = Brush.Linear(new Vector2(0f, 0f), new Vector2(1f, 0f), stops);
        var expected = Brush.Linear(new Vector2(0f, 0f), new Vector2(1f, 0f), Ramp());

        stops[1] = new GradientStop(1f, Color.Srgb(1f, 0f, 1f));

        Assert.AreEqual(expected, brush, "A brush must copy the caller's stops into an immutable payload.");
    }

    [TestMethod]
    public void GradientStops_MustBeAtLeastTwoAndOrdered()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => Brush.Linear(Vector2.Zero, Vector2.One, [new GradientStop(0f, Color.Black)]));
        Assert.ThrowsExactly<ArgumentException>(
            () => Brush.Linear(Vector2.Zero, Vector2.One, default));
        Assert.ThrowsExactly<ArgumentException>(
            () => Brush.Radial(
                Vector2.Zero,
                1f,
                [new GradientStop(0.75f, Color.Black), new GradientStop(0.25f, Color.White)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GradientStop(-0.1f, Color.Black));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GradientStop(1.1f, Color.Black));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GradientStop(float.NaN, Color.Black));
    }

    [TestMethod]
    public void GradientGeometryAndSpread_AreValidated()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Brush.Linear(new Vector2(float.NaN, 0f), Vector2.One, Ramp()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Brush.Linear(Vector2.Zero, new Vector2(0f, float.PositiveInfinity), Ramp()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Brush.Radial(Vector2.Zero, 0f, Ramp()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Brush.Radial(Vector2.Zero, -1f, Ramp()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Brush.Radial(Vector2.Zero, float.NaN, Ramp()));
        Assert.ThrowsExactly<ArgumentException>(
            () => Brush.Linear(Vector2.Zero, Vector2.One, Ramp(), (SpreadMode)int.MaxValue));
    }

    [TestMethod]
    public void Factories_ReportTheGeometryTheyWereGiven()
    {
        var linear = Brush.Linear(new Vector2(1f, 2f), new Vector2(3f, 4f), Ramp(), SpreadMode.Repeat);
        var radial = Brush.Radial(new Vector2(5f, 6f), 7f, Ramp(), SpreadMode.Mirror);
        var solid = Brush.Solid(Color.Srgb(0.1f, 0.2f, 0.3f));

        Assert.AreEqual(BrushKind.LinearGradient, linear.Kind);
        Assert.AreEqual(new Vector2(1f, 2f), linear.Start);
        Assert.AreEqual(new Vector2(3f, 4f), linear.End);
        Assert.AreEqual(SpreadMode.Repeat, linear.Spread);
        Assert.AreEqual(2, linear.Stops.Length);
        Assert.AreEqual(BrushKind.RadialGradient, radial.Kind);
        Assert.AreEqual(new Vector2(5f, 6f), radial.Center);
        Assert.AreEqual(7f, radial.Radius);
        Assert.AreEqual(SpreadMode.Mirror, radial.Spread);
        Assert.AreEqual(BrushKind.Solid, solid.Kind);
        Assert.AreEqual(Color.Srgb(0.1f, 0.2f, 0.3f), solid.Color);
    }

    [TestMethod]
    public void PatternBrush_RequiresAnImage() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => Brush.Pattern(null!));

    [TestMethod]
    public void FadeFactories_BuildTheTwoStopRampThatDoesNotBruise()
    {
        var glow = Color.Srgb(0.9f, 0.4f, 0.1f, 0.75f);

        var linear = Brush.LinearFade(new Vector2(0f, 0f), new Vector2(10f, 0f), glow, SpreadMode.Repeat);
        var radial = Brush.RadialFade(new Vector2(2f, 3f), 6f, glow);

        Assert.AreEqual(BrushKind.LinearGradient, linear.Kind);
        Assert.AreEqual(SpreadMode.Repeat, linear.Spread, "The spread mode must reach the gradient.");
        Assert.AreEqual(2, linear.Stops.Length);
        Assert.AreEqual(new GradientStop(0f, glow), linear.Stops[0]);
        Assert.AreEqual(new GradientStop(1f, glow.Fade()), linear.Stops[1]);

        Assert.AreEqual(BrushKind.RadialGradient, radial.Kind);
        Assert.AreEqual(new Vector2(2f, 3f), radial.Center);
        Assert.AreEqual(6f, radial.Radius);
        Assert.AreEqual(new GradientStop(1f, glow.Fade()), radial.Stops[1]);
        Assert.AreNotEqual(
            new GradientStop(1f, Color.Transparent),
            radial.Stops[1],
            "The far stop must keep the color's RGB, not collapse to transparent black.");
    }

    [TestMethod]
    public void FadeFactories_ValidateLikeTheirGeneralForms()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Brush.LinearFade(new Vector2(float.NaN, 0f), Vector2.One, Color.White));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Brush.RadialFade(Vector2.Zero, 0f, Color.White));
        Assert.ThrowsExactly<ArgumentException>(
            () => Brush.RadialFade(Vector2.Zero, 1f, Color.White, (SpreadMode)int.MaxValue));
    }

    private static GradientStop[] Ramp() =>
        [new GradientStop(0f, Color.Black), new GradientStop(1f, Color.White)];
}
