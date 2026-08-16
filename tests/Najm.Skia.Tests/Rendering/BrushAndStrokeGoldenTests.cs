using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class BrushAndStrokeGoldenTests
{
    // Black-to-white ramp across eight local units, sampled at pixel centers: the encoded value at
    // column x is round(255 * (x + 0.5) / 8) — 16, 48, 80, 112, 143, 175, 207, 239.
    private const string ExpectedLinearGradientHex =
        "101010ff303030ff505050ff707070ff8f8f8fffafafafffcfcfcfffefefefff";

    // White center to black at radius 2.5, clamped past it: the encoded value at a pixel center is
    // round(255 * (1 - distance / 2.5)), so (2,2) is white, (2,0) is 51, (1,1) is 111, and the four
    // corners are 2.83 units out and therefore clamped to black.
    private const string ExpectedRadialGradientHex =
        "000000ff1b1b1bff333333ff1b1b1bff000000ff" +
        "1b1b1bff6f6f6fff999999ff6f6f6fff1b1b1bff" +
        "333333ff999999ffffffffff999999ff333333ff" +
        "1b1b1bff6f6f6fff999999ff6f6f6fff1b1b1bff" +
        "000000ff1b1b1bff333333ff1b1b1bff000000ff";

    // A two-unit-wide run from (2,2) to (6,2): rows 1-2, columns 2-5 exactly.
    private const string ExpectedButtCapHex =
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "000000000000000000ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "000000000000000000ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000";

    // The same run with square caps, each extending half the stroke width: columns 1-6.
    private const string ExpectedSquareCapHex =
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00000000" +
        "0000000000ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00000000" +
        "0000000000000000000000000000000000000000000000000000000000000000";

    // A four-unit-wide corner at (2,2): the miter tip reaches (0,0), so the whole outer corner
    // square [0,4]x[0,4] is painted.
    private const string ExpectedMiterJoinHex =
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000";

    // The same corner beveled: the cut runs from (0,2) to (2,0), so the pixel centers with
    // x + y <= 2 — (0,0), (1,0), and (0,1) — drop out and nothing else changes.
    private const string ExpectedBevelJoinHex =
        "000000000000000000ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "0000000000ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000";

    // The same corner rounded by an arc of radius two about (2,2): only (0,0), 2.12 units out,
    // drops out.
    private const string ExpectedRoundJoinHex =
        "0000000000ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff00ff0000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "00ff00ff00ff00ff00ff00ff00ff00ff00000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000";

    // Two units on, two units off, starting at the contour's beginning.
    private const string ExpectedDashedStrokeHex =
        "00ff00ff00ff00ff000000000000000000ff00ff00ff00ff0000000000000000";

    [TestMethod]
    public void LinearGradientFill_MatchesRawRgbaGolden()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 1));
        var context = target.GetContext();
        var brush = Brush.Linear(
            new Vector2(0f, 0f),
            new Vector2(8f, 0f),
            [
                new GradientStop(0f, Color.Srgb(0f, 0f, 0f)),
                new GradientStop(1f, Color.Srgb(1f, 1f, 1f)),
            ]);

        context.Clear(Color.Transparent);
        context.DrawPath(Rectangle(0f, 0f, 8f, 1f), Paint.Fill(brush, isAntialias: false));

        var pixels = ReadPixels(target, 8, 1);
        Assert.AreEqual(
            ExpectedLinearGradientHex,
            Convert.ToHexString(pixels).ToLowerInvariant(),
            "A linear gradient must ramp across its axis.");
        for (var column = 1; column < 8; column++)
        {
            Assert.IsGreaterThan(
                pixels[((column - 1) * 4) + 1],
                pixels[(column * 4) + 1],
                $"Column {column} must be brighter than the one before it.");
        }
    }

    [TestMethod]
    public void RadialGradientFill_MatchesRawRgbaGolden()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(5, 5));
        var context = target.GetContext();
        var brush = Brush.Radial(
            new Vector2(2.5f, 2.5f),
            2.5f,
            [
                new GradientStop(0f, Color.Srgb(1f, 1f, 1f)),
                new GradientStop(1f, Color.Srgb(0f, 0f, 0f)),
            ]);

        context.Clear(Color.Transparent);
        context.DrawPath(Rectangle(0f, 0f, 5f, 5f), Paint.Fill(brush, isAntialias: false));

        var pixels = ReadPixels(target, 5, 5);
        Assert.AreEqual(
            ExpectedRadialGradientHex,
            Convert.ToHexString(pixels).ToLowerInvariant(),
            "A radial gradient must fall off from its center.");
        Assert.AreEqual(255, pixels[(((2 * 5) + 2) * 4) + 1], "The center stop must be reached exactly.");
        Assert.AreEqual(0, pixels[1], "The corners lie past the radius and must clamp to the last stop.");
    }

    [TestMethod]
    public void StrokeCaps_ExtendGeometryByHalfTheStrokeWidth()
    {
        var butt = StrokeRun(LineCap.Butt);
        var square = StrokeRun(LineCap.Square);

        Assert.AreEqual(ExpectedButtCapHex, Convert.ToHexString(butt).ToLowerInvariant());
        Assert.AreEqual(ExpectedSquareCapHex, Convert.ToHexString(square).ToLowerInvariant());
        Assert.AreEqual(
            CoveredPixelCount(butt) + 4,
            CoveredPixelCount(square),
            "Square caps must add one column at each end of both stroked rows.");
    }

    [TestMethod]
    public void StrokeJoins_ShapeTheOuterCorner()
    {
        var miter = StrokeCorner(LineJoin.Miter);
        var bevel = StrokeCorner(LineJoin.Bevel);
        var round = StrokeCorner(LineJoin.Round);

        Assert.AreEqual(ExpectedMiterJoinHex, Convert.ToHexString(miter).ToLowerInvariant());
        Assert.AreEqual(ExpectedBevelJoinHex, Convert.ToHexString(bevel).ToLowerInvariant());
        Assert.AreEqual(ExpectedRoundJoinHex, Convert.ToHexString(round).ToLowerInvariant());
        Assert.AreEqual(255, miter[3], "A miter join must paint the outer corner pixel.");
        Assert.AreEqual(0, bevel[3], "A bevel join must cut the outer corner pixel.");
        Assert.AreEqual(0, round[3], "A round join must cut the outer corner pixel.");
        Assert.IsGreaterThan(
            CoveredPixelCount(bevel),
            CoveredPixelCount(round),
            "A round join must keep more of the corner than a bevel.");
    }

    [TestMethod]
    public void DashedStroke_MatchesRawRgbaGolden()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 1));
        var context = target.GetContext();
        var line = new PathBuilder(initialCapacity: 2).MoveTo(0f, 0.5f).LineTo(8f, 0.5f);

        context.Clear(Color.Transparent);
        context.DrawPath(
            line,
            Paint.Stroke(
                Color.Srgb(0f, 1f, 0f),
                width: 1f,
                isAntialias: false,
                dash: new StrokeDash([2f, 2f])));

        var pixels = ReadPixels(target, 8, 1);
        Assert.AreEqual(
            ExpectedDashedStrokeHex,
            Convert.ToHexString(pixels).ToLowerInvariant(),
            "A two-on two-off dash must leave gaps along the line.");
        Assert.AreEqual(4, CoveredPixelCount(pixels), "Half of the run must remain unpainted.");
    }

    [TestMethod]
    public void EqualBrushAndDashValues_ShareOneCachedNativeObject()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 4));
        var context = (SkiaDrawContext2D)target.GetContext();
        var path = Rectangle(0f, 0f, 4f, 4f);
        var dash = new StrokeDash([1f, 1f]);
        var equalDash = new StrokeDash([1f, 1f]);

        context.DrawPath(path, Paint.Fill(Gradient()));
        context.DrawPath(path, Paint.Fill(Gradient()));
        context.DrawPath(path, Paint.Stroke(Color.White, 1f, dash: dash));
        context.DrawPath(path, Paint.Stroke(Color.White, 1f, dash: equalDash));

        Assert.AreEqual(1, context.CachedShaderCount, "Equal brush values must key one cached shader.");
        Assert.AreEqual(1, context.CachedDashCount, "Equal dash values must key one cached path effect.");

        context.DrawPath(path, Paint.Fill(Brush.Radial(Vector2.Zero, 2f, GradientStops())));
        Assert.AreEqual(2, context.CachedShaderCount, "A different brush value must add one entry.");
    }

    [TestMethod]
    public void ImagePatternBrush_FailsLoudlyWithoutCorruptingPaintState()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var path = Rectangle(0f, 0f, 2f, 2f);
        using var image = target.Snapshot();
        var pattern = Paint.Fill(Brush.Pattern(image));

        Assert.ThrowsExactly<NotSupportedException>(() => context.DrawPath(path, pattern));

        context.Clear(Color.Transparent);
        context.DrawPath(path, Paint.Fill(Color.Srgb(0f, 1f, 0f)));
        var pixels = ReadPixels(target, 2, 2);
        Assert.AreEqual(4, CoveredPixelCount(pixels), "The rejected pattern must not disturb later draws.");
    }

    [TestMethod]
    public void WarmGradientDrawLoop_AllocatesNoManagedBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var path = Rectangle(0f, 0f, 2f, 2f);
        var paint = Paint.Fill(Gradient());
        var dashed = Paint.Stroke(Gradient(), width: 1f, dash: new StrokeDash([1f, 1f]));

        // Warm twice per paint: the first draw is the cache miss that creates the native objects,
        // and the second is the first hit, which is what installs the runtime's default equality
        // comparers for the key types. Steady state begins after that.
        for (var warmup = 0; warmup < 2; warmup++)
        {
            context.DrawPath(path, paint);
            context.DrawPath(path, dashed);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            context.DrawPath(path, paint);
            context.DrawPath(path, dashed);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(
            0L,
            allocated,
            $"Value-keyed brush and dash caches allocated {allocated} managed bytes in steady state.");
    }

    private static Brush Gradient() =>
        Brush.Linear(new Vector2(0f, 0f), new Vector2(4f, 0f), GradientStops());

    private static GradientStop[] GradientStops() =>
        [
            new GradientStop(0f, Color.Srgb(1f, 0f, 0f)),
            new GradientStop(0.5f, Color.Srgb(0f, 1f, 0f)),
            new GradientStop(1f, Color.Srgb(0f, 0f, 1f)),
        ];

    private static byte[] StrokeRun(LineCap cap)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        var context = target.GetContext();
        var line = new PathBuilder(initialCapacity: 2).MoveTo(2f, 2f).LineTo(6f, 2f);

        context.Clear(Color.Transparent);
        context.DrawPath(
            line,
            Paint.Stroke(Color.Srgb(0f, 1f, 0f), width: 2f, isAntialias: false, cap: cap));

        return ReadPixels(target, 8, 4);
    }

    private static byte[] StrokeCorner(LineJoin join)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 8));
        var context = target.GetContext();
        var corner = new PathBuilder(initialCapacity: 3)
            .MoveTo(2f, 6f)
            .LineTo(2f, 2f)
            .LineTo(6f, 2f);

        context.Clear(Color.Transparent);
        context.DrawPath(
            corner,
            Paint.Stroke(Color.Srgb(0f, 1f, 0f), width: 4f, isAntialias: false, join: join));

        return ReadPixels(target, 8, 8);
    }

    private static byte[] ReadPixels(IRenderTarget target, int width, int height)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[width * height * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static int CoveredPixelCount(byte[] pixels)
    {
        var count = 0;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static PathBuilder Rectangle(float left, float top, float right, float bottom) =>
        new PathBuilder(FillRule.NonZero, initialCapacity: 5)
            .MoveTo(left, top)
            .LineTo(right, top)
            .LineTo(right, bottom)
            .LineTo(left, bottom)
            .Close();
}
