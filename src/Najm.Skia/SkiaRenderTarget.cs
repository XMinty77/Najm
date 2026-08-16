using System.Numerics;
using Najm.Core;
using SkiaSharp;

namespace Najm.Skia;

/// <summary>Owns one Skia surface and its reusable 2D draw context.</summary>
public sealed class SkiaRenderTarget : IRenderTarget
{
    private SKSurface? surface;
    private SkiaDrawContext2D? context;

    internal SkiaRenderTarget(SKSurface surface, SurfaceSpec surfaceSpec)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        SurfaceSpec = surfaceSpec;
        Size = surfaceSpec.Size;
        context = new SkiaDrawContext2D(surface.Canvas, surfaceSpec);
    }

    /// <inheritdoc />
    public PixelSize Size { get; }

    /// <inheritdoc />
    public SurfaceSpec SurfaceSpec { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Every acquisition begins a clean one-times, identity-base render pass on the same context
    /// instance. Any unbalanced state left by a prior acquisition is restored before this method
    /// returns.
    /// </remarks>
    public IDrawContext2D GetContext() => GetContext(1f);

    /// <inheritdoc />
    /// <remarks>
    /// Every acquisition begins a clean render pass on the same context instance, stamped with
    /// <paramref name="renderScale"/> and with the uniform scale of that value installed as the
    /// engine transform. Any unbalanced state left by a prior acquisition is restored before this
    /// method returns.
    /// </remarks>
    public IDrawContext2D GetContext(float renderScale)
    {
        var baseTransform = Matrix3x2.CreateScale(renderScale);
        return BeginPass(renderScale, RenderCaps.SkiaSurface, baseTransform);
    }

    /// <inheritdoc />
    public IImage Snapshot()
    {
        ObjectDisposedException.ThrowIf(surface is null, this);
        var image = surface.Snapshot()
            ?? throw new InvalidOperationException("Skia failed to snapshot the raster surface.");
        return new SkiaImage(image, Size);
    }

    /// <summary>Begins a clean internally stamped pass for a Skia driver or compositor.</summary>
    internal SkiaDrawContext2D BeginPass(
        float renderScale,
        RenderCaps caps,
        in Matrix3x2 engineBaseTransform)
    {
        ObjectDisposedException.ThrowIf(surface is null, this);
        context!.BeginPass(renderScale, caps, engineBaseTransform);
        return context;
    }

    /// <summary>Ends the active pass, restoring baseline before reporting unbalanced state.</summary>
    internal void EndPass()
    {
        ObjectDisposedException.ThrowIf(surface is null, this);
        context!.EndPass();
    }

    /// <summary>Disposes the target-owned context caches and native surface.</summary>
    public void Dispose()
    {
        var ownedContext = context;
        var ownedSurface = surface;
        context = null;
        surface = null;

        try
        {
            ownedContext?.DisposeOwnedResources();
        }
        finally
        {
            ownedSurface?.Dispose();
        }
    }
}
