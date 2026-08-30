using Najm.Core;
using SkiaSharp;
using CoreColorSpace = Najm.Core.ColorSpace;

namespace Najm.Skia;

/// <summary>Creates top-left-origin CPU-raster Skia targets.</summary>
/// <remarks>
/// The provider is single-threaded and environment-lifetime. Raster targets use analytic
/// antialiasing, so every requested sample count is normalized to one.
/// </remarks>
public sealed class RasterSkiaSurfaceProvider : ISurfaceProvider
{
    private bool disposed;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="RenderCaps.SkiaSurface"/> and nothing more. A raster target accepts a
    /// <c>SkiaDrawable</c>'s native commands, so the Skia flag is real; it is not
    /// <see cref="RenderCaps.GpuBacked"/>, which is what content holding a wrapped GL texture keys
    /// its attach-time refusal on. The value is stated even after
    /// <see cref="Dispose"/> — capabilities describe the backend this provider realizes, not whether
    /// it is still open for business, and a disposed provider that threw here would make a
    /// capability check the one thing a teardown path could not safely ask.
    /// </remarks>
    public RenderCaps Caps => RenderCaps.SkiaSurface;

    /// <inheritdoc />
    public IRenderTarget CreateTarget(in SurfaceSpec spec)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var normalizedSpec = spec.NormalizeForRaster();
        using var colorSpace = normalizedSpec.ColorSpace switch
        {
            CoreColorSpace.Srgb => SKColorSpace.CreateSrgb(),
            CoreColorSpace.LinearSrgb => SKColorSpace.CreateSrgbLinear(),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), "The color-space tag is not supported."),
        };
        var colorType = ResolveColorType(normalizedSpec.ColorSpace, nameof(spec));
        var imageInfo = new SKImageInfo(
            normalizedSpec.Width,
            normalizedSpec.Height,
            colorType,
            SKAlphaType.Premul,
            colorSpace);
        using var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);
        var surface = SKSurface.Create(imageInfo, properties)
            ?? throw new InvalidOperationException(
                $"Skia failed to create a {normalizedSpec.Width}×{normalizedSpec.Height} raster surface.");

        try
        {
            return new SkiaRenderTarget(surface, normalizedSpec);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The returned <see cref="SkiaCompositor"/> creates its layer targets and accumulation surface
    /// through this provider, so every surface a frame touches comes from one authority. It is
    /// caller-owned — in practice scene-owned — and must be disposed before the provider.
    /// </remarks>
    public ICompositor CreateCompositor()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new SkiaCompositor(this);
    }

    /// <summary>Maps one portable color-space tag onto the raster color type that carries it.</summary>
    /// <remarks>
    /// Shared with the compositor's memory accounting so the byte estimate and the allocation
    /// cannot describe different pixels.
    /// </remarks>
    /// <param name="colorSpace">The mandatory color-space tag.</param>
    /// <param name="parameterName">The caller's parameter name, for a faithful exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tag has no raster realization.</exception>
    internal static SKColorType ResolveColorType(CoreColorSpace colorSpace, string parameterName) =>
        colorSpace switch
        {
            CoreColorSpace.Srgb => SKColorType.Rgba8888,
            CoreColorSpace.LinearSrgb => SKColorType.RgbaF16,
            _ => throw new ArgumentOutOfRangeException(parameterName, "The color-space tag is not supported."),
        };

    /// <summary>Marks the provider closed to new target creation.</summary>
    /// <remarks>
    /// Callers must dispose every created target before disposing the provider. Use of a retained
    /// target after provider disposal is outside the lifetime contract and is not supported.
    /// </remarks>
    public void Dispose() => disposed = true;
}
