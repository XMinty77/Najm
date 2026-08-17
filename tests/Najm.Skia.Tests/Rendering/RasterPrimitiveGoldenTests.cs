using System.Numerics;
using System.Security.Cryptography;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class RasterPrimitiveGoldenTests
{
    private const string ExpectedRgbaHex =
        "000000ff000000ff000000ff000000ff000000ff000000ff000000ff" +
        "000000ffff0000ffff0000ffff0000ffff0000ffff0000ff000000ff" +
        "000000ffff0000ffff0000ff000000ff000000ffff0000ff000000ff" +
        "000000ffff0000ffff0000ff000000ff000000ffff0000ff000000ff" +
        "000000ff000000ffff0000ffff0000ffff0000ffff0000ff000000ff" +
        "000000ff000000ff000000ff000000ff000000ff000000ff000000ff";

    private const string ExpectedSha256 =
        "84bcabf23a5d1d6233b7f8671e44a8dceeaecb4623a020ffe60c085fd33bd3f0";

    [TestMethod]
    public void AsymmetricEvenOddPath_MatchesRawRgbaGolden()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(7, 6, sampleCount: 4, ColorSpace.Srgb));
        var context = target.GetContext();

        var background = Rectangle(0f, 0f, 7f, 6f, FillRule.NonZero);
        var shape = new PathBuilder(FillRule.EvenOdd, initialCapacity: 12)
            .MoveTo(1f, 1f)
            .LineTo(6f, 1f)
            .LineTo(6f, 5f)
            .LineTo(2f, 5f)
            .LineTo(2f, 4f)
            .LineTo(1f, 4f)
            .Close()
            .MoveTo(3f, 2f)
            .LineTo(5f, 2f)
            .LineTo(5f, 4f)
            .LineTo(3f, 4f)
            .Close();

        context.DrawPath(background, Paint.Fill(Color.Srgb(0f, 0f, 0f), isAntialias: false));
        context.DrawPath(shape, Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));

        Assert.AreSame(context, target.GetContext(), "A render target must reuse one borrowed context.");
        Assert.AreEqual(1, target.SurfaceSpec.SampleCount, "CPU raster must normalize samples to one.");

        using var snapshot = target.Snapshot();
        Span<byte> pixels = stackalloc byte[7 * 6 * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);

        var actualHex = Convert.ToHexString(pixels).ToLowerInvariant();
        var actualHash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        Assert.AreEqual(
            ExpectedRgbaHex,
            actualHex,
            "Raw RGBA mismatch. Expected rows: BBBBBBB / BRRRRRB / BRRBBRB / BRRBBRB / BBRRRRB / BBBBBBB (B=black, R=red).");
        Assert.AreEqual(ExpectedSha256, actualHash, $"Raw RGBA SHA-256 mismatch for bytes {actualHex}.");
    }

    [TestMethod]
    public void WarmDrawPathLoop_AllocatesNoManagedBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var path = Rectangle(0f, 0f, 2f, 2f, FillRule.NonZero);
        var paint = Paint.Fill(Color.Srgb(0.25f, 0.5f, 0.75f));

        context.DrawPath(path, paint);

        AllocationProbe.AssertNoneAllocated(
            1_000,
            () => context.DrawPath(path, paint),
            "Stable rewound scratch-path drawing");
    }

    [TestMethod]
    public void BasicStrokePaint_DrawsCenteredOnePixelColumn()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(3, 3));
        var context = target.GetContext();
        var line = new PathBuilder(initialCapacity: 2)
            .MoveTo(1.5f, 0f)
            .LineTo(1.5f, 3f);

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawPath(
            line,
            Paint.Stroke(Color.Srgb(0f, 1f, 0f), width: 1f, isAntialias: false));

        using var snapshot = target.Snapshot();
        var actual = new byte[3 * 3 * 4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888);
        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0,
            },
            actual);
    }

    [TestMethod]
    public void WarmPortablePrimitiveAndStateLoop_AllocatesNoManagedBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var path = Rectangle(0f, 0f, 2f, 2f, FillRule.NonZero);
        var paint = Paint.Fill(Color.Srgb(0.25f, 0.5f, 0.75f));
        var clear = Color.Srgb(0f, 0f, 0f, 0f);
        var clip = new Rect(0f, 0f, 2f, 2f);
        var transform = Matrix3x2.Identity;
        context.DrawPath(path, paint);
        using var image = target.Snapshot();

        DrawPortableSequence(context, path, paint, image, clear, clip, transform);

        AllocationProbe.AssertNoneAllocated(
            1_000,
            () => DrawPortableSequence(context, path, paint, image, clear, clip, transform),
            "The warmed portable draw and state sequence");
    }

    private static void DrawPortableSequence(
        IDrawContext2D context,
        PathBuilder path,
        in Paint paint,
        IImage image,
        Color clear,
        in Rect clip,
        in Matrix3x2 transform)
    {
        context.Clear(clear);
        context.PushTransform(transform);
        context.PushClip(clip);
        context.PushClip(path);
        context.PushOpacity(0.5f);
        context.DrawPath(path, paint);
        context.DrawImage(image, transform, ImageSampling.Nearest);
        context.PopOpacity();
        context.PopClip();
        context.PopClip();
        context.PopTransform();
    }

    private static PathBuilder Rectangle(float left, float top, float right, float bottom, FillRule fillRule) =>
        new PathBuilder(fillRule, initialCapacity: 5)
            .MoveTo(left, top)
            .LineTo(right, top)
            .LineTo(right, bottom)
            .LineTo(left, bottom)
            .Close();
}
