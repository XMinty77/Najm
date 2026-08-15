using Najm.Core;
using Najm.Utils;
using SkiaSharp;
using CoreBlendMode = Najm.Core.BlendMode;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class BlendModeLoweringTests
{
    [TestMethod]
    [DataRow(CoreBlendMode.SrcOver, SKBlendMode.SrcOver)]
    [DataRow(CoreBlendMode.Multiply, SKBlendMode.Multiply)]
    [DataRow(CoreBlendMode.Screen, SKBlendMode.Screen)]
    [DataRow(CoreBlendMode.Overlay, SKBlendMode.Overlay)]
    [DataRow(CoreBlendMode.Darken, SKBlendMode.Darken)]
    [DataRow(CoreBlendMode.Lighten, SKBlendMode.Lighten)]
    [DataRow(CoreBlendMode.ColorDodge, SKBlendMode.ColorDodge)]
    [DataRow(CoreBlendMode.ColorBurn, SKBlendMode.ColorBurn)]
    [DataRow(CoreBlendMode.HardLight, SKBlendMode.HardLight)]
    [DataRow(CoreBlendMode.SoftLight, SKBlendMode.SoftLight)]
    [DataRow(CoreBlendMode.Difference, SKBlendMode.Difference)]
    [DataRow(CoreBlendMode.Exclusion, SKBlendMode.Exclusion)]
    [DataRow(CoreBlendMode.Plus, SKBlendMode.Plus)]
    public void PortablePaintBlend_MatchesExactSkiaMode(
        CoreBlendMode portableMode,
        SKBlendMode expectedSkiaMode)
    {
        var destination = Color.Srgb(0.2f, 0.55f, 0.8f, 0.65f);
        var source = Color.Srgb(0.9f, 0.25f, 0.1f, 0.6f);
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(1, 1));
        var context = target.GetContext();
        context.Clear(destination);
        context.DrawPath(
            Rectangle(),
            Paint.Fill(source, isAntialias: false, blendMode: portableMode));

        CollectionAssert.AreEqual(
            DrawDirectSkia(destination, source, expectedSkiaMode),
            ReadRgba(target),
            $"Portable {portableMode} did not lower to Skia {expectedSkiaMode}.");
    }

    private static byte[] DrawDirectSkia(Color destination, Color source, SKBlendMode blendMode)
    {
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(
            1,
            1,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            colorSpace);
        using var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);
        using var surface = SKSurface.Create(info, properties);
        Assert.IsNotNull(surface);
        using var paint = new SKPaint();

        paint.BlendMode = SKBlendMode.Src;
        paint.SetColor(
            new SKColorF(destination.R, destination.G, destination.B, destination.A),
            colorSpace);
        surface.Canvas.DrawPaint(paint);

        paint.Reset();
        paint.IsAntialias = false;
        paint.BlendMode = blendMode;
        paint.SetColor(new SKColorF(source.R, source.G, source.B, source.A), colorSpace);
        surface.Canvas.DrawRect(SKRect.Create(1f, 1f), paint);

        var outputInfo = new SKImageInfo(
            1,
            1,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul,
            colorSpace);
        using var bitmap = new SKBitmap(outputInfo);
        Assert.IsTrue(surface.ReadPixels(outputInfo, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        var pixel = bitmap.GetPixel(0, 0);
        return [pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha];
    }

    private static PathBuilder Rectangle() =>
        new PathBuilder(initialCapacity: 5)
            .MoveTo(0f, 0f)
            .LineTo(1f, 0f)
            .LineTo(1f, 1f)
            .LineTo(0f, 1f)
            .Close();

    private static byte[] ReadRgba(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }
}
