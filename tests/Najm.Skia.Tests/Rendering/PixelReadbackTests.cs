using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class PixelReadbackTests
{
    [TestMethod]
    public void Clear_ReplacesPriorPixelWithTaggedSemiTransparentSrgb()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(1, 1, colorSpace: ColorSpace.Srgb));
        var context = target.GetContext();
        context.DrawPath(
            Rectangle(1f, 1f),
            Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false));

        context.Clear(Color.Srgb(1f, 0.5f, 0.25f, 0.5f));

        using var snapshot = target.Snapshot();
        var actual = new byte[4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888);
        CollectionAssert.AreEqual(new byte[] { 255, 128, 64, 128 }, actual);
    }

    [TestMethod]
    public void Clear_ConvertsTaggedSrgbIntoLinearF16TargetBeforeReadback()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(
            new SurfaceSpec(1, 1, colorSpace: ColorSpace.LinearSrgb));

        target.GetContext().Clear(Color.Srgb(0.75f, 0.5f, 0.25f, 0.5f));

        using var snapshot = target.Snapshot();
        var actual = new byte[4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888);
        AssertByteWithin(191, actual[0], 1, "red");
        AssertByteWithin(128, actual[1], 1, "green");
        AssertByteWithin(64, actual[2], 1, "blue");
        AssertByteWithin(128, actual[3], 1, "alpha");
    }

    [TestMethod]
    public void SemiTransparentSrgbPixel_DistinguishesByteAndAlphaLayouts()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(1, 1, colorSpace: ColorSpace.Srgb));
        var context = target.GetContext();
        var pixel = Rectangle(1f, 1f);
        var paint = Paint.Fill(Color.Srgb(1f, 0.5f, 0.25f, 0.5f), isAntialias: false);
        context.DrawPath(pixel, paint);

        using var snapshot = target.Snapshot();
        var rgba = new byte[4];
        var rgbaPremul = new byte[4];
        var bgraPremul = new byte[4];
        snapshot.CopyPixels(rgba, PixelFormat.Rgba8888);
        snapshot.CopyPixels(rgbaPremul, PixelFormat.Rgba8888Premul);
        snapshot.CopyPixels(bgraPremul, PixelFormat.Bgra8888Premul);

        CollectionAssert.AreEqual(new byte[] { 255, 128, 64, 128 }, rgba);
        CollectionAssert.AreEqual(new byte[] { 128, 64, 32, 128 }, rgbaPremul);
        CollectionAssert.AreEqual(new byte[] { 32, 64, 128, 128 }, bgraPremul);
    }

    [TestMethod]
    public void LinearF16Target_ReadsBackAsTaggedSrgbBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(
            new SurfaceSpec(1, 1, colorSpace: ColorSpace.LinearSrgb));
        var context = target.GetContext();
        context.DrawPath(
            Rectangle(1f, 1f),
            Paint.Fill(Color.Srgb(0.75f, 0.5f, 0.25f, 0.5f), isAntialias: false));

        using var snapshot = target.Snapshot();
        var actual = new byte[4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888);

        Assert.AreEqual(ColorSpace.LinearSrgb, target.SurfaceSpec.ColorSpace);
        // F16 linear-light storage plus transfer-function conversion can move a channel by one
        // 8-bit code when the unpremultiplied sRGB result is quantized.
        AssertByteWithin(191, actual[0], 1, "red");
        AssertByteWithin(128, actual[1], 1, "green");
        AssertByteWithin(64, actual[2], 1, "blue");
        AssertByteWithin(128, actual[3], 1, "alpha");
    }

    [TestMethod]
    public void DefaultPaint_IsTransparentSourceOverNoOp()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(1, 1));
        var context = target.GetContext();
        var pixel = Rectangle(1f, 1f);
        context.DrawPath(pixel, Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        context.DrawPath(pixel, default);

        using var snapshot = target.Snapshot();
        var actual = new byte[4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888);

        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, actual);
    }

    private static PathBuilder Rectangle(float width, float height) =>
        new PathBuilder(initialCapacity: 5)
            .MoveTo(0f, 0f)
            .LineTo(width, 0f)
            .LineTo(width, height)
            .LineTo(0f, height)
            .Close();

    private static void AssertByteWithin(byte expected, byte actual, byte tolerance, string channel)
    {
        var delta = Math.Abs(expected - actual);
        Assert.IsLessThanOrEqualTo(
            tolerance,
            delta,
            $"Expected {channel}={expected}±{tolerance}, actual {actual}.");
    }
}
