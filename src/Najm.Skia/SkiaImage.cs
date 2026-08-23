using Najm.Core;
using SkiaSharp;
using CorePixelFormat = Najm.Core.PixelFormat;

namespace Najm.Skia;

/// <summary>Owns an immutable Skia image snapshot and provides explicit pixel readback.</summary>
/// <remarks>
/// This is the <em>immutable</em> kind of <see cref="IImage"/>: a snapshot whose pixels never
/// change. The externally owned kind, whose pixels the author rewrites between draws, is
/// <see cref="GlTextureImage"/>.
/// </remarks>
public sealed class SkiaImage : IImage, ISkiaNativeImage
{
    private SKImage? image;

    internal SkiaImage(SKImage image, PixelSize size)
    {
        this.image = image ?? throw new ArgumentNullException(nameof(image));
        Size = size;
    }

    /// <inheritdoc />
    public PixelSize Size { get; }

    /// <summary>Gets the live native snapshot, throwing once this wrapper has been disposed.</summary>
    internal SKImage GetNativeImage()
    {
        ObjectDisposedException.ThrowIf(image is null, this);
        return image;
    }

    /// <inheritdoc />
    SKImage ISkiaNativeImage.GetNativeImage() => GetNativeImage();

    /// <inheritdoc />
    public unsafe void CopyPixels(Span<byte> destination, CorePixelFormat format)
    {
        ObjectDisposedException.ThrowIf(image is null, this);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The requested pixel format is not defined.", nameof(format));
        }

        var rowBytes = checked(Size.Width * 4);
        var requiredBytes = checked(rowBytes * Size.Height);
        if (destination.Length < requiredBytes)
        {
            throw new ArgumentException(
                $"Pixel destination requires at least {requiredBytes} bytes but contains {destination.Length}.",
                nameof(destination));
        }

        var (colorType, alphaType) = format switch
        {
            CorePixelFormat.Rgba8888 => (SKColorType.Rgba8888, SKAlphaType.Unpremul),
            CorePixelFormat.Rgba8888Premul => (SKColorType.Rgba8888, SKAlphaType.Premul),
            CorePixelFormat.Bgra8888Premul => (SKColorType.Bgra8888, SKAlphaType.Premul),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        using var outputColorSpace = SKColorSpace.CreateSrgb();
        var outputInfo = new SKImageInfo(Size.Width, Size.Height, colorType, alphaType, outputColorSpace);
        fixed (byte* destinationPointer = destination)
        {
            if (!image.ReadPixels(outputInfo, (IntPtr)destinationPointer, rowBytes, 0, 0))
            {
                throw new InvalidOperationException("Skia failed to copy image pixels in the requested format.");
            }
        }
    }

    /// <summary>Releases the owned immutable native image.</summary>
    public void Dispose()
    {
        image?.Dispose();
        image = null;
    }
}
