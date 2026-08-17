using System.Numerics;
using Najm.Core;
using SkiaSharp;

namespace Najm.Skia;

/// <summary>Owns one Skia surface and its reusable 2D draw context.</summary>
public sealed class SkiaRenderTarget : IRenderTarget
{
    private SKSurface? surface;
    private SKCanvas? canvas;
    private SkiaDrawContext2D? context;

    internal SkiaRenderTarget(SKSurface surface, SurfaceSpec surfaceSpec)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        SurfaceSpec = surfaceSpec;
        Size = surfaceSpec.Size;

        // Held for the target's lifetime deliberately. SkiaSharp caches the managed canvas wrapper
        // for a native surface weakly, so re-reading SKSurface.Canvas allocates a fresh wrapper
        // whenever a collection has swept the old one — an allocation in the middle of a warm
        // render loop, arriving only after a GC and therefore only intermittently.
        canvas = surface.Canvas;
        context = new SkiaDrawContext2D(canvas, surfaceSpec);
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

    /// <summary>
    /// Begins a clean pass on a surface that holds one offset sub-rectangle of the frame.
    /// </summary>
    /// <remarks>
    /// The compositor uses this for a layer that occupies a viewport: the traverser installs
    /// absolute frame-device transforms either way, and <paramref name="deviceOffset"/> brings the
    /// viewport's device origin onto this surface's origin.
    /// </remarks>
    internal SkiaDrawContext2D BeginPass(
        float renderScale,
        RenderCaps caps,
        in Matrix3x2 engineBaseTransform,
        in Matrix3x2 deviceOffset)
    {
        ObjectDisposedException.ThrowIf(surface is null, this);
        context!.BeginPass(renderScale, caps, engineBaseTransform, deviceOffset);
        return context;
    }

    /// <summary>
    /// Gets the owned native surface, for the compositor's surface-to-surface draws.
    /// </summary>
    /// <remarks>
    /// Reading a surface this way is what keeps the compositor free of <see cref="Snapshot"/>: a
    /// surface that is still being written this frame is drawn from directly, never copied into an
    /// image first.
    /// </remarks>
    internal SKSurface NativeSurface
    {
        get
        {
            ObjectDisposedException.ThrowIf(surface is null, this);
            return surface;
        }
    }

    /// <summary>Gets the owned surface's canvas at whatever state the current pass left it in.</summary>
    internal SKCanvas NativeCanvas
    {
        get
        {
            ObjectDisposedException.ThrowIf(canvas is null, this);
            return canvas;
        }
    }

    /// <summary>Abandons any active pass and restores the canvas to its surface baseline.</summary>
    internal void RestoreBaseline()
    {
        ObjectDisposedException.ThrowIf(surface is null, this);
        context!.RestoreBaseline();
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

        // The canvas belongs to the surface and is released with it, never disposed separately.
        canvas = null;

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
