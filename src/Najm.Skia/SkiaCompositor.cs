using System.Numerics;
using Najm.Core;
using SkiaSharp;

namespace Najm.Skia;

/// <summary>Composites one scene's layer stack onto a Skia output target.</summary>
/// <remarks>
/// <para>
/// This type is backend-facing engine machinery, not an authoring API. It is created by
/// <see cref="RasterSkiaSurfaceProvider.CreateCompositor"/>, owned by the scene that acquired it,
/// and disposed with that scene, taking every surface it holds with it.
/// </para>
/// <para>
/// It realizes the canonical algorithm of <see cref="ICompositor"/> without re-implementing any
/// node semantics: each visible layer is staged through its own persistent target — cleared to the
/// layer's <see cref="Layer.ClearColor"/>, then painted by the shared
/// <see cref="RenderTraverser"/> — and merged into one accumulation surface with the layer's
/// <see cref="Layer.Opacity"/> and <see cref="Layer.Blend"/>, placed 1:1 into its
/// <see cref="Layer.Viewport"/> when it has one. A final replace-blit moves the accumulation
/// surface onto the output.
/// </para>
/// <para>
/// Every read of a surface still being written this frame is an
/// <c>SKCanvas.DrawSurface</c> — the read-while-write primitive — so the loop takes no snapshot:
/// snapshotting a surface that is written afterwards would force a copy of its whole backing once
/// per frame.
/// </para>
/// <para>
/// M1 scope: ordinary layers only. Backdrop-reading layers, masks, layer effects, and surface
/// pooling are not implemented and are not silently approximated. Node-tier isolation is not this
/// type's business at all: the shared <see cref="RenderTraverser"/> opens a unit bracket per
/// isolating node while it walks, so a composited frame gets the same units a direct frame does
/// without this type knowing they exist.
/// </para>
/// </remarks>
public sealed class SkiaCompositor : ICompositor
{
    /// <summary>
    /// The largest surface extent this compositor will ask a provider for, in device pixels. It is
    /// far above any real output and exists so an absurd render scale fails as an argument error
    /// rather than as an integer overflow inside surface creation.
    /// </summary>
    private const int MaximumSurfaceExtent = 1 << 16;

    private readonly ISurfaceProvider provider;
    private readonly List<LayerEntry> entries = [];
    private readonly Dictionary<Layer, LayerEntry> entriesByLayer = [];
    private readonly SKPaint mergePaint = new();
    private readonly SKPaint blitPaint = new();
    private SkiaRenderTarget? accumulation;
    private SurfaceSpec accumulationRequest;
    private int merges;
    private int acquisitions;
    private bool usedFastPath;
    private bool disposed;

    internal SkiaCompositor(ISurfaceProvider provider) =>
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public CompositorStats Stats { get; private set; }

    /// <inheritdoc />
    public CompositorDebugOptions Debug { get; } = new();

    /// <inheritdoc />
    public void Render(
        LayerStack layers,
        IRenderTarget output,
        in Vector2 virtualResolution,
        float renderScale)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(output);
        EnsureVirtualResolution(virtualResolution);
        EnsureRenderScale(renderScale);

        if (output is not SkiaRenderTarget target)
        {
            throw new ArgumentException(
                "The output target must originate from a compatible Skia surface provider.",
                nameof(output));
        }

        // Read once: a debug option that changes mid-frame must not split the frame between paths.
        var forceCanonicalPath = Debug.ForceCanonicalPath;
        merges = 0;
        acquisitions = 0;
        usedFastPath = false;

        ReleaseTargetsOfRemovedLayers(layers);
        var frameSize = ResolveFrameSize(virtualResolution, renderScale);

        var soloLayer = forceCanonicalPath ? null : FindSingleLayerFastPathLayer(layers, target, frameSize);
        if (soloLayer is not null)
        {
            RenderToOutputDirectly(soloLayer, target, virtualResolution, renderScale);
            usedFastPath = true;
        }
        else
        {
            RenderStaged(layers, target, virtualResolution, renderScale, frameSize);
        }

        PublishStats();
    }

    /// <summary>Disposes every layer target, the accumulation surface, and the merge paints.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        for (var index = 0; index < entries.Count; index++)
        {
            entries[index].Target?.Dispose();
        }

        entries.Clear();
        entriesByLayer.Clear();
        accumulation?.Dispose();
        accumulation = null;
        mergePaint.Dispose();
        blitPaint.Dispose();
    }

    /// <summary>
    /// Runs the canonical staged algorithm: clear the accumulation surface, stage and merge every
    /// contributing layer bottom to top, then replace the output with the result.
    /// </summary>
    private void RenderStaged(
        LayerStack layers,
        SkiaRenderTarget output,
        in Vector2 virtualResolution,
        float renderScale,
        PixelSize frameSize)
    {
        var accumulationTarget = AcquireAccumulation(output);
        accumulationTarget.RestoreBaseline();
        accumulationTarget.NativeCanvas.Clear(SKColors.Transparent);

        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            if (!RenderTraverser.ParticipatesInRender(layer))
            {
                // Normative: an invisible or fully transparent layer is not bound, not cleared, and
                // not merged. Its ClearColor is content it does not get to contribute.
                continue;
            }

            var placement = ResolvePlacement(layer, frameSize, renderScale);
            var layerTarget = AcquireLayerTarget(layer, placement, output.SurfaceSpec);
            StageLayer(layerTarget, layer, placement, virtualResolution, renderScale);
            MergeIntoAccumulation(accumulationTarget, layerTarget, layer, placement);
            merges++;
        }

        // The output is write-only: this replaces every pixel of it, so whatever the host left there
        // cannot reach the frame.
        output.RestoreBaseline();
        blitPaint.Reset();
        blitPaint.BlendMode = SKBlendMode.Src;
        output.NativeCanvas.DrawSurface(accumulationTarget.NativeSurface, 0f, 0f, blitPaint);
    }

    /// <summary>
    /// Renders one layer into its own target: clear to the layer's color, then run the shared
    /// traverser. A layer whose subtree draws nothing still lands its clear color here.
    /// </summary>
    /// <remarks>
    /// The traverser installs absolute frame-device transforms for every layer, viewport'd or not —
    /// including a viewport'd world layer's camera, which frames the viewport and is placed at the
    /// viewport's frame origin by <see cref="RenderTraverser.ComputeLayerBase"/>. The scene's
    /// virtual resolution is therefore what it is handed; <see cref="Placement.DeviceOffset"/> is
    /// what brings the viewport's device origin onto this smaller surface.
    /// </remarks>
    private static void StageLayer(
        SkiaRenderTarget layerTarget,
        Layer layer,
        in Placement placement,
        in Vector2 virtualResolution,
        float renderScale)
    {
        var baseTransform = Matrix3x2.CreateScale(renderScale);
        var context = layerTarget.BeginPass(
            renderScale,
            RenderCaps.SkiaSurface,
            baseTransform,
            placement.DeviceOffset);
        context.Clear(layer.ClearColor);
        RenderTraverser.RenderLayer(layer, context, virtualResolution, renderScale);

        // Not in a finally: if the walk threw, the frame is already lost and the next pass on this
        // target restores the baseline anyway, whereas ending the pass here would replace the
        // author's exception with an unbalanced-state one.
        layerTarget.EndPass();
    }

    /// <summary>
    /// Merges one staged layer target into the accumulation surface with the layer's opacity and
    /// blend, placed at its device origin.
    /// </summary>
    /// <remarks>
    /// Opacity rides the paint's alpha and blend rides the paint's blend mode, which is exactly the
    /// group semantics the architecture gives a layer: the whole staged layer is attenuated and
    /// blended as one image, never per drawn primitive. The draw is a pure integer-offset placement,
    /// so the layer's pixels survive it unresampled.
    /// </remarks>
    private void MergeIntoAccumulation(
        SkiaRenderTarget accumulationTarget,
        SkiaRenderTarget layerTarget,
        Layer layer,
        in Placement placement)
    {
        mergePaint.Reset();
        mergePaint.BlendMode = SkiaDrawContext2D.ToSkiaBlendMode(layer.Blend);
        mergePaint.ColorF = new SKColorF(0f, 0f, 0f, layer.Opacity);
        accumulationTarget.NativeCanvas.DrawSurface(
            layerTarget.NativeSurface,
            placement.X,
            placement.Y,
            mergePaint);
    }

    /// <summary>
    /// Renders the one qualifying layer straight into the output — FP-1, which skips the layer
    /// target, the accumulation surface, and both blits.
    /// </summary>
    /// <remarks>
    /// The clear is the staged path's clear of the layer target: the canonical path merges that
    /// target over a transparent accumulation surface at opacity one with the default blend, which
    /// reproduces the target exactly, and then replaces the output with it. Clearing the output and
    /// painting into it is the same frame with two copies removed.
    /// </remarks>
    private static void RenderToOutputDirectly(
        Layer layer,
        SkiaRenderTarget output,
        in Vector2 virtualResolution,
        float renderScale)
    {
        var context = output.BeginPass(
            renderScale,
            RenderCaps.SkiaSurface,
            Matrix3x2.CreateScale(renderScale));
        context.Clear(layer.ClearColor);
        RenderTraverser.RenderLayer(layer, context, virtualResolution, renderScale);
        output.EndPass();
    }

    /// <summary>
    /// Returns the single layer that qualifies for FP-1, or null when the canonical path must run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate: exactly one layer contributes to this frame; it frames the whole frame rather
    /// than a viewport; its opacity is one and its blend is the default, so the merge would be an
    /// identity; and the target it would have been staged through has the output's own surface
    /// specification, so painting on the output rasterizes identically. Layer effects and backdrop
    /// reads would also disqualify a layer; neither exists in M1.
    /// </para>
    /// <para>
    /// The specification match is the whole normalized specification. Two of its three components
    /// are settled before the comparison: a staged layer target is created in the output's own
    /// sample count and color space, already in the provider's normalized form — which for a raster
    /// provider means every sample count has collapsed to one — so they cannot differ. What is left
    /// to decide is whether the layer's frame covers exactly the output's drawable area, and that is
    /// the comparison made here. It is a component comparison rather than a built
    /// <see cref="SurfaceSpec"/>, because constructing one validates its color tag, and that
    /// validation re-materializes the enum's metadata after every collection: an allocation in the
    /// middle of a warm render loop.
    /// </para>
    /// </remarks>
    private static Layer? FindSingleLayerFastPathLayer(
        LayerStack layers,
        SkiaRenderTarget output,
        PixelSize frameSize)
    {
        Layer? candidate = null;
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            if (!RenderTraverser.ParticipatesInRender(layer))
            {
                continue;
            }
            if (candidate is not null)
            {
                return null;
            }

            candidate = layer;
        }

        if (candidate is null ||
            candidate.Viewport is not null ||
            candidate.Opacity != 1f ||
            candidate.Blend != BlendMode.SrcOver)
        {
            return null;
        }

        var outputSize = output.Size;
        return frameSize.Width == outputSize.Width && frameSize.Height == outputSize.Height
            ? candidate
            : null;
    }

    /// <summary>
    /// Returns the layer's persistent target, acquiring one on first use and re-acquiring it when
    /// the surface it needs has changed shape.
    /// </summary>
    /// <remarks>
    /// The requested specification carries the layer's device size — the frame for an ordinary
    /// layer, its own viewport for a viewport'd one — and the output's sample count and color space,
    /// which makes the merge a same-space draw. Re-acquisition therefore tracks exactly the reasons
    /// a caller can observe: render scale, virtual resolution, viewport size, and output
    /// specification. The kept request is compared component-wise against what this frame needs, so
    /// a steady state neither builds a <see cref="SurfaceSpec"/> nor allocates.
    /// </remarks>
    private SkiaRenderTarget AcquireLayerTarget(
        Layer layer,
        in Placement placement,
        in SurfaceSpec outputSpec)
    {
        if (!entriesByLayer.TryGetValue(layer, out var entry))
        {
            entry = new LayerEntry(layer);
            entriesByLayer.Add(layer, entry);
            entries.Add(entry);
        }

        if (entry.Target is not null && DescribesSurface(entry.Request, placement.Size, outputSpec))
        {
            return entry.Target;
        }

        var request = new SurfaceSpec(
            placement.Size.Width,
            placement.Size.Height,
            outputSpec.SampleCount,
            outputSpec.ColorSpace);
        entry.Target?.Dispose();
        entry.Target = null;
        var created = CreateTarget(request);
        entry.Target = created;
        entry.Request = request;
        entry.Bytes = EstimateBytes(created.SurfaceSpec);
        acquisitions++;
        return created;
    }

    /// <summary>
    /// Returns the accumulation surface: the output's own size and specification, which makes it the
    /// merge space by construction and the final blit a 1:1 replace.
    /// </summary>
    private SkiaRenderTarget AcquireAccumulation(SkiaRenderTarget output)
    {
        var outputSpec = output.SurfaceSpec;
        if (accumulation is not null && DescribesSurface(accumulationRequest, output.Size, outputSpec))
        {
            return accumulation;
        }

        var request = new SurfaceSpec(
            output.Size.Width,
            output.Size.Height,
            outputSpec.SampleCount,
            outputSpec.ColorSpace);
        accumulation?.Dispose();
        accumulation = null;
        accumulation = CreateTarget(request);
        accumulationRequest = request;
        acquisitions++;
        return accumulation;
    }

    /// <summary>Disposes the targets of layers that have left the stack since the last render.</summary>
    /// <remarks>
    /// Membership in the stack is the test, not participation in this frame: hiding a layer must not
    /// throw its target away and unhiding it must not have to allocate one again.
    /// </remarks>
    private void ReleaseTargetsOfRemovedLayers(LayerStack layers)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (Contains(layers, entry.Layer))
            {
                continue;
            }

            entries.RemoveAt(index);
            entriesByLayer.Remove(entry.Layer);
            entry.Target?.Dispose();
        }
    }

    private SkiaRenderTarget CreateTarget(in SurfaceSpec request)
    {
        var created = provider.CreateTarget(request);
        if (created is SkiaRenderTarget target)
        {
            return target;
        }

        created.Dispose();
        throw new InvalidOperationException(
            "A Skia compositor requires targets created by a Skia surface provider.");
    }

    /// <summary>
    /// Returns whether one kept request already describes a surface of this size in this source's
    /// sample count and color space.
    /// </summary>
    /// <remarks>
    /// Component-wise on purpose. Building a <see cref="SurfaceSpec"/> to compare would validate its
    /// color tag, and that validation re-materializes the enum's metadata after every collection —
    /// a warm frame must not allocate merely to discover it needs nothing. A default request has no
    /// width and therefore describes no surface, which is what makes the first frame acquire.
    /// </remarks>
    private static bool DescribesSurface(in SurfaceSpec request, PixelSize size, in SurfaceSpec source) =>
        request.Width == size.Width &&
        request.Height == size.Height &&
        request.SampleCount == source.SampleCount &&
        request.ColorSpace == source.ColorSpace;

    private void PublishStats()
    {
        var targetCount = 0;
        var targetBytes = 0L;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Target is null)
            {
                continue;
            }

            targetCount++;
            targetBytes += entry.Bytes;
        }

        Stats = new CompositorStats
        {
            LayerTargetCount = targetCount,
            LayerTargetBytes = targetBytes,
            TargetAcquisitionCount = acquisitions,
            MergeCount = merges,
            UsedSingleLayerFastPath = usedFastPath,
        };
    }

    /// <summary>
    /// Estimates one surface's resident color storage. Color only, by contract: mip, stencil, and
    /// driver overhead are not portably knowable and are not guessed at.
    /// </summary>
    private static long EstimateBytes(in SurfaceSpec spec)
    {
        var colorType = RasterSkiaSurfaceProvider.ResolveColorType(spec.ColorSpace, nameof(spec));
        var bytesPerPixel = new SKImageInfo(1, 1, colorType).BytesPerPixel;
        return (long)spec.Width * spec.Height * bytesPerPixel * spec.SampleCount;
    }

    /// <summary>Returns where one layer's target sits in the frame and how large it is.</summary>
    /// <remarks>
    /// A full-frame layer's target is the frame. A viewport'd layer's target is
    /// <c>ceil(viewport size × renderScale)</c> at the viewport's rounded device origin: the origin
    /// is an integer so that both the render and the merge land on the frame's own pixel grid, which
    /// is what makes the placement 1:1 rather than a resample.
    /// </remarks>
    private static Placement ResolvePlacement(Layer layer, PixelSize frameSize, float renderScale)
    {
        if (layer.Viewport is not { } viewport)
        {
            return new Placement(frameSize, 0, 0);
        }

        var x = RoundToDevice(viewport.X * renderScale, nameof(Layer.Viewport));
        var y = RoundToDevice(viewport.Y * renderScale, nameof(Layer.Viewport));
        var width = CeilToExtent(viewport.Width * renderScale, nameof(Layer.Viewport));
        var height = CeilToExtent(viewport.Height * renderScale, nameof(Layer.Viewport));
        return new Placement(new PixelSize(width, height), x, y);
    }

    private static PixelSize ResolveFrameSize(in Vector2 virtualResolution, float renderScale) =>
        new(
            CeilToExtent(virtualResolution.X * renderScale, nameof(virtualResolution)),
            CeilToExtent(virtualResolution.Y * renderScale, nameof(virtualResolution)));

    /// <summary>Rounds a device coordinate to the pixel grid, rejecting values a surface cannot hold.</summary>
    private static int RoundToDevice(float value, string parameterName)
    {
        var rounded = MathF.Round(value);
        if (!float.IsFinite(rounded) || MathF.Abs(rounded) > MaximumSurfaceExtent)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A device coordinate must be finite and within the largest supported surface extent.");
        }

        return (int)rounded;
    }

    /// <summary>
    /// Converts a device extent to whole pixels, rounding outward so a fractional edge is covered
    /// rather than cropped, and never below one pixel.
    /// </summary>
    private static int CeilToExtent(float value, string parameterName)
    {
        var ceiling = MathF.Ceiling(value);
        if (!float.IsFinite(ceiling) || ceiling > MaximumSurfaceExtent)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A device extent must be finite and within the largest supported surface extent.");
        }

        return ceiling < 1f ? 1 : (int)ceiling;
    }

    private static bool Contains(LayerStack layers, Layer layer)
    {
        for (var index = 0; index < layers.Count; index++)
        {
            if (ReferenceEquals(layers[index], layer))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureVirtualResolution(in Vector2 virtualResolution)
    {
        if (!float.IsFinite(virtualResolution.X) ||
            !float.IsFinite(virtualResolution.Y) ||
            virtualResolution.X <= 0f ||
            virtualResolution.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualResolution),
                virtualResolution,
                "A virtual resolution must have finite, positive components.");
        }
    }

    private static void EnsureRenderScale(float renderScale)
    {
        if (!float.IsFinite(renderScale) || renderScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderScale),
                renderScale,
                "A render scale must be finite and positive.");
        }
    }

    /// <summary>Where one layer's target sits in the frame, in whole device pixels.</summary>
    private readonly record struct Placement
    {
        internal Placement(PixelSize size, int x, int y)
        {
            Size = size;
            X = x;
            Y = y;
        }

        internal PixelSize Size { get; }

        internal int X { get; }

        internal int Y { get; }

        /// <summary>
        /// Gets the frame-to-surface translation the staged pass renders through, the exact inverse
        /// of the placement the merge draws with.
        /// </summary>
        internal Matrix3x2 DeviceOffset => Matrix3x2.CreateTranslation(-X, -Y);
    }

    /// <summary>One layer's persistent target and the request that produced it.</summary>
    private sealed class LayerEntry(Layer layer)
    {
        internal Layer Layer { get; } = layer;

        internal SkiaRenderTarget? Target { get; set; }

        internal SurfaceSpec Request { get; set; }

        /// <summary>
        /// Gets or sets the target's estimated color-storage bytes, measured once at acquisition so
        /// reporting a frame's memory costs nothing.
        /// </summary>
        internal long Bytes { get; set; }
    }
}
