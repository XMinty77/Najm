using Najm.Core;
using SkiaSharp;
using CorePixelFormat = Najm.Core.PixelFormat;

namespace Najm.Skia;

/// <summary>Encodes one leased frame to a PNG file through Skia's encoder.</summary>
internal static class SkiaPngWriter
{
    /// <summary>Writes a frame to <paramref name="path"/>, creating its directory if needed.</summary>
    /// <param name="pixels">The frame to encode. Ownership stays with the caller.</param>
    /// <param name="path">The absolute or relative destination file path.</param>
    /// <exception cref="InvalidOperationException">Skia declined to encode the frame.</exception>
    internal static void Write(PixelFrameLease pixels, string path)
    {
        var (colorType, alphaType) = MapFormat(pixels.Format);
        using var colorSpace = SKColorSpace.CreateSrgb();
        var imageInfo = new SKImageInfo(pixels.Width, pixels.Height, colorType, alphaType, colorSpace);

        using var image = SKImage.FromPixelCopy(imageInfo, (ReadOnlySpan<byte>)pixels.Pixels, pixels.Stride)
            ?? throw new InvalidOperationException(
                $"Skia could not wrap a {pixels.Width}×{pixels.Height} {pixels.Format} frame for encoding.");
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException(
                $"Skia failed to encode a {pixels.Width}×{pixels.Height} PNG for '{path}'.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(file);
    }

    private static (SKColorType ColorType, SKAlphaType AlphaType) MapFormat(CorePixelFormat format) =>
        format switch
        {
            CorePixelFormat.Rgba8888 => (SKColorType.Rgba8888, SKAlphaType.Unpremul),
            CorePixelFormat.Rgba8888Premul => (SKColorType.Rgba8888, SKAlphaType.Premul),
            CorePixelFormat.Bgra8888Premul => (SKColorType.Bgra8888, SKAlphaType.Premul),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The frame pixel format has no Skia realization."),
        };
}
