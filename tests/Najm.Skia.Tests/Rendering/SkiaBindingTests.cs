using SkiaSharp;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class SkiaBindingTests
{
    [TestMethod]
    public void NativeLibraryLoads_AndRasterSurfaceRetainsSrgbTag()
    {
        Assert.IsTrue(
            SkiaSharpVersion.CheckNativeLibraryCompatible(),
            $"Managed binding requires {SkiaSharpVersion.NativeMinimum}, native library is {SkiaSharpVersion.Native}.");

        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(4, 3, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
        using var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);
        using var surface = SKSurface.Create(info, properties);
        Assert.IsNotNull(surface);

        using var snapshot = surface.Snapshot();
        Assert.IsNotNull(snapshot.ColorSpace);
        Assert.IsTrue(snapshot.ColorSpace.IsSrgb);
        Assert.AreEqual(SKAlphaType.Premul, snapshot.AlphaType);
        Assert.AreEqual(SKColorType.Rgba8888, snapshot.ColorType);
    }

    [TestMethod]
    public void OrdinaryBoundedSaveLayer_RestoresPaintedContent()
    {
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
        using var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);
        using var surface = SKSurface.Create(info, properties);
        using var restorePaint = new SKPaint { BlendMode = SKBlendMode.SrcOver };
        using var redPaint = new SKPaint { Color = SKColors.Red };

        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SaveLayer(SKRect.Create(1f, 1f, 6f, 6f), restorePaint);
        surface.Canvas.DrawRect(SKRect.Create(1f, 1f, 6f, 6f), redPaint);
        surface.Canvas.Restore();
        surface.Canvas.Flush();

        using var bitmap = new SKBitmap(info);
        Assert.IsTrue(surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(4, 4));
        Assert.AreEqual((byte)0, bitmap.GetPixel(0, 0).Alpha);
    }
}
