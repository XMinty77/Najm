using Najm.Core;
using SkiaSharp;
using CorePixelFormat = Najm.Core.PixelFormat;

namespace Najm.Skia;

/// <summary>Decodes an image file into a leased frame through Skia's codecs.</summary>
/// <remarks>
/// The inverse of <see cref="SkiaPngWriter"/>, and the reason frame diagnostics need a backend at
/// all: the arithmetic over pixels is portable, but turning a PNG on disk back into pixels is not.
/// </remarks>
internal static class SkiaPngReader
{
    /// <summary>Decodes <paramref name="path"/> into a freshly rented lease the caller owns.</summary>
    /// <param name="path">The image file to decode. Any format Skia's codecs recognize is accepted.</param>
    /// <param name="format">The byte and alpha layout to decode into.</param>
    /// <remarks>
    /// <para>
    /// The destination <see cref="SKImageInfo"/> carries no colour space on purpose. Naming one
    /// would ask Skia to colour-manage the decode, and a file tagged with any profile — or an
    /// untagged file, which Skia then assumes is sRGB — would come back with transformed bytes. A
    /// diagnostic must return the pixels the file holds, or a frame written by
    /// <see cref="SkiaPngWriter"/> and read straight back could fail its own byte-identity check.
    /// Alpha conversion is not colour management and does still happen: PNG stores straight alpha,
    /// so asking for a premultiplied format premultiplies, which is a requested change of meaning
    /// rather than an incidental one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> has no Skia realization.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">Skia could not decode the file.</exception>
    internal static PixelFrameLease Read(string path, CorePixelFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"There is no image file at '{fullPath}'.", fullPath);
        }

        var (colorType, alphaType) = MapFormat(format);
        using var encoded = SKData.Create(fullPath)
            ?? throw new InvalidDataException($"'{fullPath}' could not be read.");
        using var codec = SKCodec.Create(encoded)
            ?? throw new InvalidDataException(
                $"'{fullPath}' is not an image Skia's codecs recognize.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, colorType, alphaType);
        using var bitmap = SKBitmap.Decode(codec, info)
            ?? throw new InvalidDataException(
                $"Skia decoded no pixels from '{fullPath}' ({codec.Info.Width}×{codec.Info.Height}).");

        var lease = PixelFrameLease.Rent(bitmap.Width, bitmap.Height, format);
        try
        {
            var source = bitmap.GetPixelSpan();
            var sourceStride = bitmap.RowBytes;
            for (var y = 0; y < bitmap.Height; y++)
            {
                source.Slice(y * sourceStride, lease.RowBytes).CopyTo(lease.Row(y));
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        return lease;
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
