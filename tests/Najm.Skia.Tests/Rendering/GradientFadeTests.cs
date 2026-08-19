using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Demonstrates the gradient-to-transparent trap on real pixels, and pins the ergonomics that make
/// the correct form the easy one.
/// </summary>
/// <remarks>
/// <para>
/// Gradient stops interpolate straight (unpremultiplied) color: R, G, B, and A each move
/// independently between adjacent stops. Ending a ramp at <see cref="Color.Transparent"/> therefore
/// ends it at transparent <em>black</em> and walks RGB down to zero alongside alpha, so every
/// partially transparent sample in between is darker than the color the author asked for. Composited,
/// that reads as a grey bruise through what should be a clean fade — and it looks like a rendering
/// bug rather than the API misuse it is, which is why the engine now ships
/// <see cref="Color.Fade"/>, <see cref="Brush.LinearFade"/>, and <see cref="Brush.RadialFade"/>.
/// </para>
/// <para>
/// The interpolation model itself is not the bug and is not changed here: straight interpolation is
/// what SVG, CSS, Skia, and PDF all specify, and switching to premultiplied would be a different
/// rendering model, not a fix.
/// </para>
/// </remarks>
[TestClass]
public sealed class GradientFadeTests
{
    /// <summary>
    /// The ramp's start color. 0.8 encodes as exactly 204 of 255, and every value the derivation
    /// below produces is an exact eight-bit number, so the expectations carry no rounding question
    /// and do not depend on where in its pipeline the backend quantizes.
    /// </summary>
    private static readonly Color Base = Color.Srgb(0.8f, 0.8f, 0.8f);

    // Surface is 3x1 and the gradient axis runs from x = 0.5 to x = 2.5, so the three pixel centers
    // sample the ramp at t = 0, t = 0.5, and t = 1 exactly. The surface is cleared to opaque black,
    // so source-over in encoded sRGB leaves out = src.rgb · src.a.
    //
    // Fading to Color.Transparent: at t the stop pair gives rgb = 0.8·(1 − t) and a = 1 − t, so the
    // midpoint is rgb 0.4 (= 102) at a = 0.5, composited to 51 = 0x33. The midtone lost 40% of the
    // brightness the author asked for; that loss is the bruise.
    private const string BruisedRowHex = "ccccccff" + "333333ff" + "000000ff";

    // Fading to the same color at zero alpha: rgb stays 0.8 (= 204) at every t and only a falls, so
    // the midpoint composites to 204 · 0.5 = 102 = 0x66. This is the correct one: "fades out" means
    // coverage falls, not that the color turns to soot on the way.
    private const string CleanRowHex = "ccccccff" + "666666ff" + "000000ff";

    [TestMethod]
    public void FadingToTransparentBlackDragsAGreyBruiseThroughTheRamp()
    {
        var bruised = RenderRamp(Color.Transparent);
        var clean = RenderRamp(Base.Fade());

        Assert.AreEqual(BruisedRowHex, Hex(bruised), "A ramp to Color.Transparent must show the bruise.");
        Assert.AreEqual(CleanRowHex, Hex(clean), "A ramp to the same RGB at zero alpha must not.");

        var bruisedMidpoint = bruised[4];
        var cleanMidpoint = clean[4];
        Assert.AreNotEqual(
            cleanMidpoint,
            bruisedMidpoint,
            "The two ramps must differ at the midpoint; if they did not there would be no trap.");
        Assert.AreEqual(
            51,
            bruisedMidpoint,
            "Fading to transparent black composites the midpoint at 0.8 · 0.5 · 0.5 · 255.");
        Assert.AreEqual(
            102,
            cleanMidpoint,
            "Fading to the same RGB at zero alpha composites it at 0.8 · 0.5 · 255 — the correct value.");
        Assert.AreEqual(
            2d,
            (double)cleanMidpoint / bruisedMidpoint,
            1e-9d,
            "The correct midpoint is exactly twice the bruised one, because the bruise multiplies the "
            + "midtone by its own alpha a second time.");
        Assert.AreEqual(255, bruised[3], "The opaque backdrop must survive both ramps.");
        Assert.AreEqual(255, clean[3], "The opaque backdrop must survive both ramps.");
    }

    [TestMethod]
    public void TheTrapIsInvisibleOnBlack_WhichIsWhyItSurvivesReview()
    {
        // The two spellings coincide only when the color is already black, so a glow prototyped on a
        // black subject looks perfect and only bruises once someone tints it.
        Assert.AreEqual(
            Color.Transparent,
            Color.Black.Fade(),
            "Black at zero alpha is transparent black; that coincidence is what hides the bug.");
        Assert.AreNotEqual(
            Color.Transparent,
            Base.Fade(),
            "For every other color the two differ, and the difference is the bruise.");
    }

    [TestMethod]
    public void LinearFadeBuildsExactlyTheCorrectRamp()
    {
        var start = new Vector2(0.5f, 0f);
        var end = new Vector2(2.5f, 0f);

        var factory = Brush.LinearFade(start, end, Base);
        var handWritten = Brush.Linear(
            start,
            end,
            [new GradientStop(0f, Base), new GradientStop(1f, Base.Fade())]);

        Assert.AreEqual(handWritten, factory, "The factory must build the ramp the docs tell authors to write.");
        Assert.AreEqual(CleanRowHex, Hex(RenderRamp(factory)), "And it must rasterize as the clean ramp.");
        Assert.AreNotEqual(
            Brush.Linear(start, end, [new GradientStop(0f, Base), new GradientStop(1f, Color.Transparent)]),
            factory,
            "It must not be the bruised ramp.");
    }

    [TestMethod]
    public void RadialFadeBuildsExactlyTheCorrectRamp()
    {
        var center = new Vector2(4f, 4f);
        const float Radius = 4f;
        var glow = Color.Srgb(1f, 0.82f, 0.35f, 0.9f);

        var factory = Brush.RadialFade(center, Radius, glow);
        var stops = factory.Stops;

        Assert.AreEqual(
            Brush.Radial(center, Radius, [new GradientStop(0f, glow), new GradientStop(1f, glow.Fade())]),
            factory,
            "The soft-glow factory must build the ramp whose RGB never moves.");
        Assert.HasCount(2, stops.ToArray());
        Assert.AreEqual(glow.A, stops[0].Color.A, "The center keeps the color's own alpha as the peak.");
        Assert.AreEqual(0f, stops[1].Color.A, "The rim reaches zero alpha.");
        Assert.AreEqual(glow.R, stops[1].Color.R, "The rim keeps the hue.");
        Assert.AreEqual(glow.G, stops[1].Color.G, "The rim keeps the hue.");
        Assert.AreEqual(glow.B, stops[1].Color.B, "The rim keeps the hue.");
    }

    [TestMethod]
    public void AShapedFalloffKeepsTheSameRuleAtItsFarEnd()
    {
        // The factories cover the straight ramp; a glow that wants a curve still writes its own
        // stops, and the rule about the far end is what the docs carry. Every stop here is the same
        // RGB, so no sample between them can be off-hue.
        var glow = Color.Srgb(1f, 0.82f, 0.35f);
        Span<GradientStop> stops = stackalloc GradientStop[5];
        for (var index = 0; index < stops.Length; index++)
        {
            var t = index / (float)(stops.Length - 1);
            var falloff = (1f - (t * t)) * (1f - (t * t));
            stops[index] = new GradientStop(t, glow.WithAlpha(falloff));
        }

        var brush = Brush.Radial(new Vector2(8f, 8f), 8f, stops);

        foreach (var stop in brush.Stops)
        {
            Assert.AreEqual(glow.R, stop.Color.R, "Every stop must carry the same red channel.");
            Assert.AreEqual(glow.G, stop.Color.G, "Every stop must carry the same green channel.");
            Assert.AreEqual(glow.B, stop.Color.B, "Every stop must carry the same blue channel.");
        }

        Assert.AreEqual(0f, brush.Stops[^1].Color.A, "The ramp still has to reach zero alpha at the rim.");
    }

    private static byte[] RenderRamp(Color endColor) =>
        RenderRamp(Brush.Linear(
            new Vector2(0.5f, 0f),
            new Vector2(2.5f, 0f),
            [new GradientStop(0f, Base), new GradientStop(1f, endColor)]));

    private static byte[] RenderRamp(Brush brush)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(3, 1));
        var context = target.GetContext();

        context.Clear(Color.Black);
        context.DrawRect(new Rect(0f, 0f, 3f, 1f), Paint.Fill(brush, isAntialias: false));

        using var snapshot = target.Snapshot();
        var pixels = new byte[3 * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static string Hex(byte[] pixels) => Convert.ToHexString(pixels).ToLowerInvariant();
}
