using System.Numerics;
using System.Text;
using Najm.Core;
using SkiaSharp;
using CoreBlendMode = Najm.Core.BlendMode;
using CoreBrush = Najm.Core.Brush;
using CoreBrushKind = Najm.Core.BrushKind;
using CoreFillRule = Najm.Core.FillRule;
using CoreImageSampling = Najm.Core.ImageSampling;
using CoreLineCap = Najm.Core.LineCap;
using CoreLineJoin = Najm.Core.LineJoin;
using CorePaint = Najm.Core.Paint;
using CorePaintStyle = Najm.Core.PaintStyle;
using CorePathBuilder = Najm.Core.PathBuilder;
using CoreRect = Najm.Core.Rect;
using CoreSpreadMode = Najm.Core.SpreadMode;
using CoreStrokeDash = Najm.Core.StrokeDash;
using UtilsColor = Najm.Utils.Color;

namespace Najm.Skia;

/// <summary>Lowers portable Tier-1 drawing commands onto a target-owned Skia canvas.</summary>
/// <remarks>
/// <para>
/// Tier-1 only: this class lowers <c>Clear</c>, <c>DrawPath</c>, and <c>DrawImage</c>, and inherits
/// every Tier-2 convenience from <see cref="DrawContext2DBase"/> unchanged. ARCHITECTURE §7.2
/// permits overriding one — <c>DrawCircle</c> onto <c>SKCanvas.DrawCircle</c> is the example it
/// gives — and none is overridden here, deliberately: a native oval rasterizes differently from
/// the four-cubic path an author writes by hand, and the guarantee worth more than the marginal
/// quality is that a convenience and its explicit Tier-1 spelling land on identical pixels.
/// </para>
/// <para>
/// A <see cref="SkiaRenderTarget"/> owns and reuses this object. Authors receive it as a borrowed
/// <see cref="IDrawContext2D"/> and must not retain it after the target is disposed. One native
/// scratch path and paint are backend-owned, rewound or reset, and reused for every draw. Rewinding
/// the path retains its native storage for allocation-free drawing at a stable command count.
/// State pushes share one strict typed LIFO stack and are balanced automatically when the owning
/// target is disposed. Engine brackets — layer brackets around a whole layer, unit brackets around
/// one isolating node's subtree — share a second typed LIFO stack of their own, apart from author
/// state: the engine installs its per-node transforms inside them, which author state may never
/// allow. The native objects Skia forces us to allocate for brushes and dashes live in
/// context-owned caches keyed by the portable descriptor <em>value</em>: the first appearance of a
/// gradient or dash allocates, every repetition is a dictionary hit that allocates nothing, and the
/// caches are bounded so an animated descriptor cannot pin native memory frame after frame.
/// </para>
/// </remarks>
public sealed class SkiaDrawContext2D : DrawContext2DBase
{
    private const int InitialStateCapacity = 16;

    /// <summary>
    /// How many distinct brush values, and how many distinct dash values, one context keeps a native
    /// object for before it starts evicting the least recently used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why bounded at all.</strong> NAJM-SKIA II.2 requires these caches to trim "so an
    /// abandoned gradient doesn't pin GPU memory forever". There is no surface pool and no epoch in
    /// this tree to trim against, so the bound does the job instead — and the case that makes it
    /// urgent is not abandonment but animation. A brush whose stop colours are tweened is a
    /// <em>different</em> brush value on every frame, so at 60 fps an unbounded dictionary gains
    /// 3,600 shaders a minute and never releases one; a dash whose phase marches does the same.
    /// </para>
    /// <para>
    /// <strong>Why 64.</strong> The bound has to sit above the number of distinct descriptor values
    /// drawn in a <em>single</em> frame, because below that every entry is evicted before its next
    /// use and the cache degrades into construct-per-draw. That number is palette-sized rather than
    /// node-sized: §1.4 budgets a few hundred to ~3,000 nodes per scene, and nodes share brushes
    /// rather than each inventing one, so a scene with a dozen distinct gradients is already a busy
    /// one. 64 leaves roughly a 5× margin over that while staying cheap to hold: a gradient shader
    /// of a handful of stops is a few hundred bytes of native state, so 64 of them is tens of
    /// kilobytes per context, against the tens of megabytes one 4K layer target costs. One number
    /// serves both caches because both hold the same kind of thing for the same reason, and a dash
    /// interval array is smaller than a gradient ramp, never larger.
    /// </para>
    /// <para>
    /// A scene that genuinely draws more than 64 distinct gradients per frame is not broken by this;
    /// it pays construction per draw, exactly as it would have on its first frame today. Its fix is
    /// the batch tier, not a bigger cache.
    /// </para>
    /// </remarks>
    private const int DescriptorCacheCapacity = 64;

    /// <summary>
    /// Initial depth of the engine bracket stack. Layers nest shallowly and isolating nodes nest
    /// only as deep as an author stacks composition properties, so this is generous already; the
    /// stack doubles on overflow exactly as the author state stack does.
    /// </summary>
    private const int InitialBracketCapacity = 16;

    /// <summary>
    /// Native save slots one engine layer bracket occupies: the viewport clip and the group layer.
    /// </summary>
    private const int SaveSlotsPerLayerBracket = 2;

    /// <summary>
    /// Native save slots one engine unit bracket occupies: the group layer alone. A node has no
    /// viewport to clip and no target of its own to clear, so it needs no second slot.
    /// </summary>
    private const int SaveSlotsPerUnitBracket = 1;

    private static readonly SKSamplingOptions LinearSampling = new(
        SKFilterMode.Linear,
        SKMipmapMode.None);
    private static readonly SKSamplingOptions NearestSampling = new(
        SKFilterMode.Nearest,
        SKMipmapMode.None);

    private readonly SKCanvas canvas;
    private readonly int baseSaveCount;
    private readonly SKPath nativePath = new();
    private readonly SKPaint nativePaint = new();
    private readonly SKColorSpace srgbColorSpace = SKColorSpace.CreateSrgb();
    private readonly DescriptorCache<CoreBrush, SKShader> shaderCache = new(DescriptorCacheCapacity);
    private readonly DescriptorCache<CoreStrokeDash, SKPathEffect> dashCache =
        new(DescriptorCacheCapacity);
    private StateKind[] stateStack = new StateKind[InitialStateCapacity];
    private BracketKind[] bracketStack = new BracketKind[InitialBracketCapacity];
    private Matrix3x2 deviceOffset = Matrix3x2.Identity;
    private RenderCaps caps;
    private float renderScale;
    private int stateDepth;
    private int engineBracketDepth;
    private int engineSlotBaseline;
    private bool passActive;
    private bool disposed;

    internal SkiaDrawContext2D(SKCanvas canvas, SurfaceSpec surfaceSpec)
    {
        this.canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        baseSaveCount = canvas.SaveCount;
        engineSlotBaseline = baseSaveCount;
        SurfaceSpec = surfaceSpec;
    }

    /// <inheritdoc />
    public override SurfaceSpec SurfaceSpec { get; }

    /// <inheritdoc />
    public override RenderCaps Caps
    {
        get
        {
            EnsureActive();
            return caps;
        }
    }

    /// <inheritdoc />
    public override float RenderScale
    {
        get
        {
            EnsureActive();
            return renderScale;
        }
    }

    /// <inheritdoc />
    public override float Scale
    {
        get
        {
            EnsureActive();
            var matrix = canvas.TotalMatrix;
            var determinant =
                ((double)matrix.ScaleX * matrix.ScaleY) -
                ((double)matrix.SkewX * matrix.SkewY);
            var scale = Math.Sqrt(Math.Abs(determinant)) / RenderScale;
            if (!double.IsFinite(scale) || scale > float.MaxValue)
            {
                throw new InvalidOperationException("The current transform scale is not representable as a finite float.");
            }

            return (float)scale;
        }
    }

    /// <inheritdoc />
    public override void Clear(UtilsColor color)
    {
        EnsureActive();
        StampColor(color, SKBlendMode.Src);
        canvas.DrawPaint(nativePaint);
    }

    /// <inheritdoc />
    public override void DrawPath(CorePathBuilder path, in CorePaint paint)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(path);

        LowerPath(path);
        StampPathPaint(paint);
        canvas.DrawPath(nativePath, nativePaint);
    }

    /// <inheritdoc />
    public override void DrawImage(
        IImage image,
        in Matrix3x2 imageToLocal,
        CoreImageSampling sampling = CoreImageSampling.Linear)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(image);
        EnsureFiniteMatrix(imageToLocal, nameof(imageToLocal));
        var nativeSampling = sampling switch
        {
            CoreImageSampling.Linear => LinearSampling,
            CoreImageSampling.Nearest => NearestSampling,
            _ => throw new ArgumentException("The image sampling mode is not defined.", nameof(sampling)),
        };
        if (image is not SkiaImage skiaImage)
        {
            throw new ArgumentException(
                "The image must originate from a compatible Skia backend.",
                nameof(image));
        }

        var source = skiaImage.GetNativeImage();
        var matrix = ToSkiaMatrix(imageToLocal);
        nativePaint.Reset();
        nativePaint.BlendMode = SKBlendMode.SrcOver;

        var saveCount = canvas.Save();
        try
        {
            canvas.Concat(matrix);
            canvas.DrawImage(source, 0f, 0f, nativeSampling, nativePaint);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The canvas matrix is replaced with <paramref name="engineToDevice"/> at the pass baseline
    /// save slot, so the save count the pass was begun with is preserved and ending the pass still
    /// restores the surface exactly. Requiring an empty author stack makes the balancing rule
    /// structural instead of a debug-only assertion. A pass begun on a surface that holds an offset
    /// sub-rectangle of the frame composes its device offset below this transform; a full-frame pass
    /// has none and installs exactly what it is handed.
    /// </remarks>
    public override void SetEngineTransform(in Matrix3x2 engineToDevice)
    {
        EnsureActive();
        EnsureFiniteMatrix(engineToDevice, nameof(engineToDevice));
        if (stateDepth != 0)
        {
            throw new InvalidOperationException(
                $"Cannot set the engine transform while {stateDepth} unbalanced context state push(es) remain ({DescribeStack()}); author state must be balanced within Render.");
        }

        InstallEngineTransform(engineToDevice);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two native save slots realize one bracket, both below the engine transform's own slot. The
    /// outer slot holds the viewport clip, resolved through the pass's device offset and then left
    /// behind — a clip is stored in device space, so restoring the matrix afterwards keeps the
    /// engine transform composing from the same baseline it always did. The inner slot is a
    /// <c>SaveLayer</c> whose paint carries the bracket's opacity and blend, which is what makes
    /// them apply to the layer as a group when it closes rather than to each primitive inside it;
    /// the clear is filled inside that layer, over transparency, exactly as the compositor clears a
    /// freshly bound layer target. A transparent clear is skipped because source-over with a zero
    /// alpha source is the identity, not because it is close enough.
    /// </remarks>
    public override void BeginLayerBracket(in LayerBracket bracket)
    {
        EnsureActive();
        if (stateDepth != 0)
        {
            throw new InvalidOperationException(
                $"Cannot open a layer bracket while {stateDepth} unbalanced context state push(es) remain ({DescribeStack()}); author state must be balanced within Render.");
        }

        // Lowered before the canvas moves, so an undefined blend cannot leave a half-open bracket,
        // and the stack is grown before it too, so a resize cannot fail over an open group.
        var blendMode = ToSkiaBlendMode(bracket.Blend);
        EnsureBracketCapacity();
        try
        {
            if (canvas.SaveCount > engineSlotBaseline)
            {
                canvas.RestoreToCount(engineSlotBaseline);
            }

            canvas.Save();
            if (bracket.Viewport is { } viewport)
            {
                var baselineMatrix = canvas.TotalMatrix;
                canvas.Concat(ToSkiaMatrix(deviceOffset));
                canvas.ClipRect(
                    SKRect.Create(viewport.X, viewport.Y, viewport.Width, viewport.Height),
                    SKClipOperation.Intersect,
                    antialias: false);
                canvas.SetMatrix(baselineMatrix);
            }

            StampColor(new UtilsColor(1f, 1f, 1f, bracket.Opacity), blendMode);
            canvas.SaveLayer(nativePaint);
            if (bracket.Clear.A > 0f)
            {
                StampColor(bracket.Clear, SKBlendMode.SrcOver);
                canvas.DrawPaint(nativePaint);
            }

            engineSlotBaseline += SaveSlotsPerLayerBracket;
            bracketStack[engineBracketDepth++] = BracketKind.Layer;
        }
        catch
        {
            // Back to where a clean open would have started: the bracket's slots are gone and so is
            // the engine transform the open shed, which the caller reinstalls either way.
            if (canvas.SaveCount > engineSlotBaseline)
            {
                canvas.RestoreToCount(engineSlotBaseline);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public override void EndLayerBracket() => EndBracket(BracketKind.Layer);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One native save slot realizes a unit, below the engine transform's own slot: a
    /// <c>SaveLayer</c> whose paint carries the unit's opacity and blend, applied to everything the
    /// subtree drew when the layer restores. That single restore is what makes
    /// <see cref="Node2D.Opacity"/> group opacity — the subtree is composited into the layer at full
    /// alpha and the layer's alpha is applied to the composite, so overlapping siblings are
    /// attenuated once between them rather than once each.
    /// </para>
    /// <para>
    /// The layer takes no bounds, which is Skia's spelling of the conservative M1 sizing: the group
    /// covers the current clip. Whatever clip an enclosing layer bracket's viewport installed is
    /// still in force at the unit's baseline, so a unit inside a viewport'd layer is already bounded
    /// by that viewport rather than by the whole surface.
    /// </para>
    /// </remarks>
    public override void BeginUnitBracket(in UnitBracket bracket)
    {
        EnsureActive();
        if (stateDepth != 0)
        {
            throw new InvalidOperationException(
                $"Cannot open a unit bracket while {stateDepth} unbalanced context state push(es) remain ({DescribeStack()}); author state must be balanced within Render.");
        }

        // Lowered before the canvas moves, so an undefined blend cannot leave a half-open bracket,
        // and the stack is grown before it too, so a resize cannot fail over an open group.
        var blendMode = ToSkiaBlendMode(bracket.Blend);
        EnsureBracketCapacity();
        try
        {
            if (canvas.SaveCount > engineSlotBaseline)
            {
                canvas.RestoreToCount(engineSlotBaseline);
            }

            StampColor(new UtilsColor(1f, 1f, 1f, bracket.Opacity), blendMode);
            canvas.SaveLayer(nativePaint);
            engineSlotBaseline += SaveSlotsPerUnitBracket;
            bracketStack[engineBracketDepth++] = BracketKind.Unit;
        }
        catch
        {
            // Back to where a clean open would have started: the unit's slot is gone and so is the
            // engine transform the open shed, which the caller reinstalls either way.
            if (canvas.SaveCount > engineSlotBaseline)
            {
                canvas.RestoreToCount(engineSlotBaseline);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public override void EndUnitBracket() => EndBracket(BracketKind.Unit);

    /// <summary>
    /// Closes the innermost engine bracket, which must be of <paramref name="expected"/> kind.
    /// </summary>
    /// <remarks>
    /// Layer and unit brackets share one last-in-first-out order because they share the canvas save
    /// stack, so closing them out of order would restore another bracket's slots. The kind check
    /// turns that into a named error rather than a silently wrong frame.
    /// </remarks>
    private void EndBracket(BracketKind expected)
    {
        EnsureActive();
        var name = DescribeBracket(expected);
        if (engineBracketDepth == 0)
        {
            throw new InvalidOperationException(
                $"Cannot end a {name} bracket because no engine {name} bracket is open.");
        }

        var innermost = bracketStack[engineBracketDepth - 1];
        if (innermost != expected)
        {
            throw new InvalidOperationException(
                $"Cannot end a {name} bracket before the more recently opened engine {DescribeBracket(innermost)} bracket.");
        }
        if (stateDepth != 0)
        {
            throw new InvalidOperationException(
                $"Cannot end a {name} bracket while {stateDepth} unbalanced context state push(es) remain ({DescribeStack()}); author state must be balanced within Render.");
        }

        // Restoring past the bracket's own slots also sheds whatever engine transform slot the walk
        // left installed inside it, which is the one thing above them.
        var bracketBaseline = engineSlotBaseline - SaveSlotsFor(expected);
        canvas.RestoreToCount(bracketBaseline);
        engineSlotBaseline = bracketBaseline;
        bracketStack[--engineBracketDepth] = BracketKind.None;
    }

    /// <inheritdoc />
    public override void PushTransform(in Matrix3x2 localTransform)
    {
        EnsureActive();
        EnsureFiniteMatrix(localTransform, nameof(localTransform));
        EnsureStateCapacity();
        var matrix = ToSkiaMatrix(localTransform);

        var saveCount = canvas.Save();
        try
        {
            canvas.Concat(matrix);
            CommitPush(StateKind.Transform);
        }
        catch
        {
            canvas.RestoreToCount(saveCount);
            throw;
        }
    }

    /// <inheritdoc />
    public override void PopTransform() => Pop(StateKind.Transform, "transform");

    /// <inheritdoc />
    public override void PushClip(in CoreRect bounds)
    {
        EnsureActive();
        EnsureStateCapacity();
        var nativeBounds = SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height);

        var saveCount = canvas.Save();
        try
        {
            canvas.ClipRect(nativeBounds, SKClipOperation.Intersect, antialias: true);
            CommitPush(StateKind.Clip);
        }
        catch
        {
            canvas.RestoreToCount(saveCount);
            throw;
        }
    }

    /// <inheritdoc />
    public override void PushClip(CorePathBuilder path)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(path);
        LowerPath(path);
        EnsureStateCapacity();

        var saveCount = canvas.Save();
        try
        {
            canvas.ClipPath(nativePath, SKClipOperation.Intersect, antialias: true);
            CommitPush(StateKind.Clip);
        }
        catch
        {
            canvas.RestoreToCount(saveCount);
            throw;
        }
    }

    /// <inheritdoc />
    public override void PopClip() => Pop(StateKind.Clip, "clip");

    /// <inheritdoc />
    public override void PushOpacity(float opacity)
    {
        EnsureActive();
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be finite and within [0, 1].");
        }

        EnsureStateCapacity();
        var saveCount = canvas.SaveCount;
        try
        {
            if (opacity < 1f)
            {
                StampColor(new UtilsColor(1f, 1f, 1f, opacity), SKBlendMode.SrcOver);
                canvas.SaveLayer(nativePaint);
            }
            else
            {
                canvas.Save();
            }
            CommitPush(StateKind.Opacity);
        }
        catch
        {
            if (canvas.SaveCount > saveCount)
            {
                canvas.RestoreToCount(saveCount);
            }
            throw;
        }
    }

    /// <inheritdoc />
    public override void PopOpacity() => Pop(StateKind.Opacity, "opacity");

    internal void BeginPass(
        float renderScale,
        RenderCaps caps,
        in Matrix3x2 engineBaseTransform) =>
        BeginPass(renderScale, caps, engineBaseTransform, Matrix3x2.Identity);

    /// <summary>
    /// Begins a pass whose surface holds a sub-rectangle of the frame, offset from the frame origin.
    /// </summary>
    /// <param name="renderScale">The finite positive device-pixel scale stamped on the pass.</param>
    /// <param name="caps">The capabilities the pass advertises.</param>
    /// <param name="engineBaseTransform">The engine transform installed as the pass baseline.</param>
    /// <param name="deviceOffset">
    /// The frame-to-surface mapping composed <em>below</em> every engine transform installed during
    /// this pass, including the ones the render traverser sets per node. A layer that occupies a
    /// viewport renders through the same absolute frame-device transforms as a full-frame layer and
    /// this translation brings the viewport's device origin to the surface's origin, so the surface
    /// holds the frame's pixels 1:1 and the compositor's merge is a pure placement.
    /// </param>
    internal void BeginPass(
        float renderScale,
        RenderCaps caps,
        in Matrix3x2 engineBaseTransform,
        in Matrix3x2 deviceOffset)
    {
        EnsureNotDisposed();
        if (!float.IsFinite(renderScale) || renderScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderScale),
                "Render scale must be finite and positive.");
        }
        const RenderCaps definedCaps =
            RenderCaps.SkiaSurface |
            RenderCaps.VectorTarget |
            RenderCaps.GpuBacked;
        if ((caps & ~definedCaps) != 0 || (caps & RenderCaps.SkiaSurface) == 0)
        {
            throw new ArgumentException(
                "Skia render caps must include SkiaSurface and contain only defined flags.",
                nameof(caps));
        }
        EnsureFiniteMatrix(engineBaseTransform, nameof(engineBaseTransform));
        EnsureFiniteMatrix(deviceOffset, nameof(deviceOffset));

        // Every argument is validated before any field moves, so a rejected stamp cannot disturb an
        // already active pass.
        ResetToBaseline();
        this.deviceOffset = deviceOffset;
        var saveCount = canvas.SaveCount;
        try
        {
            InstallEngineTransform(engineBaseTransform);
            this.renderScale = renderScale;
            this.caps = caps;
            passActive = true;
        }
        catch
        {
            if (canvas.SaveCount > saveCount)
            {
                canvas.RestoreToCount(saveCount);
            }
            passActive = false;
            throw;
        }
    }

    internal void EndPass()
    {
        EnsureNotDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("No render pass is active.");
        }

        var unbalancedStateCount = stateDepth;
        var unbalancedLayerCount = CountBrackets(BracketKind.Layer);
        var unbalancedUnitCount = CountBrackets(BracketKind.Unit);
        ResetToBaseline();
        if (unbalancedStateCount != 0 || unbalancedLayerCount != 0 || unbalancedUnitCount != 0)
        {
            throw new InvalidOperationException(
                $"The render pass ended with {DescribeImbalance(unbalancedStateCount, unbalancedLayerCount, unbalancedUnitCount)}; baseline state was restored.");
        }
    }

    /// <summary>
    /// Names what a pass ended holding. Author pushes, layer brackets, and unit brackets are counted
    /// separately because they are owned by different callers — the author, the layer walk, and the
    /// node walk — and a fix for one is not a fix for another.
    /// </summary>
    private static string DescribeImbalance(int stateCount, int layerCount, int unitCount)
    {
        var parts = new List<string>(3);
        if (stateCount != 0)
        {
            parts.Add($"{stateCount} unbalanced context state push(es)");
        }
        if (layerCount != 0)
        {
            parts.Add($"{layerCount} unbalanced engine layer bracket(s)");
        }
        if (unitCount != 0)
        {
            parts.Add($"{unitCount} unbalanced engine unit bracket(s)");
        }

        return parts.Count switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}",
        };
    }

    /// <summary>Counts the open engine brackets of one kind.</summary>
    private int CountBrackets(BracketKind kind)
    {
        var count = 0;
        for (var index = 0; index < engineBracketDepth; index++)
        {
            if (bracketStack[index] == kind)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Abandons any active pass and returns the canvas to the state it was constructed in.
    /// </summary>
    /// <remarks>
    /// A compositor draws surface to surface on the bare canvas, outside any pass, and must know
    /// that no engine transform, clip, or unbalanced author state is installed when it does.
    /// Unlike <see cref="EndPass"/> this reports nothing and demands nothing: it is a recovery, not
    /// a contract check.
    /// </remarks>
    internal void RestoreBaseline()
    {
        EnsureNotDisposed();
        ResetToBaseline();
        deviceOffset = Matrix3x2.Identity;
    }

    internal void DisposeOwnedResources()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            ResetToBaseline();
        }
        finally
        {
            disposed = true;
            nativePath.Dispose();
            nativePaint.Dispose();
            shaderCache.Clear();
            dashCache.Clear();
            srgbColorSpace.Dispose();
        }
    }

    /// <summary>Gets how many brush values currently hold a cached native shader.</summary>
    internal int CachedShaderCount => shaderCache.Count;

    /// <summary>Gets how many dash values currently hold a cached native path effect.</summary>
    internal int CachedDashCount => dashCache.Count;

    /// <summary>Gets the bound both descriptor caches hold to, for tests that pin it.</summary>
    internal static int DescriptorCacheBound => DescriptorCacheCapacity;

    /// <summary>Gets how many cached shaders have been evicted and disposed over this context's life.</summary>
    internal int EvictedShaderCount => shaderCache.EvictionCount;

    /// <summary>Gets how many cached dash effects have been evicted and disposed over this context's life.</summary>
    internal int EvictedDashCount => dashCache.EvictionCount;

    private void StampPathPaint(in CorePaint paint)
    {
        var style = paint.Style switch
        {
            CorePaintStyle.Fill => SKPaintStyle.Fill,
            CorePaintStyle.Stroke => SKPaintStyle.Stroke,
            _ => throw new ArgumentOutOfRangeException(nameof(paint), "The paint style is not supported."),
        };
        var blendMode = ToSkiaBlendMode(paint.BlendMode);
        var cap = ToSkiaStrokeCap(paint.Cap);
        var join = ToSkiaStrokeJoin(paint.Join);

        // Resolved before the reset so a failed lowering never leaves a half-stamped paint.
        var shader = paint.Brush is { Kind: not CoreBrushKind.Solid } brush ? GetShader(brush) : null;
        var pathEffect = paint.Dash is { } dash ? GetDashEffect(dash) : null;

        nativePaint.Reset();
        nativePaint.IsAntialias = paint.IsAntialias;
        nativePaint.Style = style;
        nativePaint.StrokeWidth = paint.StrokeWidth;
        nativePaint.StrokeCap = cap;
        nativePaint.StrokeJoin = join;
        nativePaint.StrokeMiter = paint.MiterLimit;
        nativePaint.BlendMode = blendMode;
        nativePaint.SetColor(
            new SKColorF(paint.Color.R, paint.Color.G, paint.Color.B, paint.Color.A),
            srgbColorSpace);
        if (shader is not null)
        {
            nativePaint.Shader = shader;
        }
        if (pathEffect is not null)
        {
            nativePaint.PathEffect = pathEffect;
        }
    }

    private SKShader GetShader(in CoreBrush brush)
    {
        if (shaderCache.TryGet(brush, out var cached))
        {
            return cached;
        }

        var created = CreateShader(brush);
        shaderCache.Add(brush, created);
        return created;
    }

    private SKShader CreateShader(in CoreBrush brush)
    {
        if (brush.Kind == CoreBrushKind.ImagePattern)
        {
            throw new NotSupportedException(
                "Image pattern brushes are not yet implemented by the Skia backend.");
        }

        var stops = brush.Stops;
        var colors = new SKColorF[stops.Length];
        var offsets = new float[stops.Length];
        for (var index = 0; index < stops.Length; index++)
        {
            var stop = stops[index];
            colors[index] = new SKColorF(stop.Color.R, stop.Color.G, stop.Color.B, stop.Color.A);
            offsets[index] = stop.Offset;
        }

        var tileMode = ToSkiaTileMode(brush.Spread);
        var shader = brush.Kind switch
        {
            CoreBrushKind.LinearGradient => SKShader.CreateLinearGradient(
                new SKPoint(brush.Start.X, brush.Start.Y),
                new SKPoint(brush.End.X, brush.End.Y),
                colors,
                srgbColorSpace,
                offsets,
                tileMode),
            CoreBrushKind.RadialGradient => SKShader.CreateRadialGradient(
                new SKPoint(brush.Center.X, brush.Center.Y),
                brush.Radius,
                colors,
                srgbColorSpace,
                offsets,
                tileMode),
            _ => throw new ArgumentOutOfRangeException(nameof(brush), "The brush kind is not supported."),
        };

        return shader ?? throw new InvalidOperationException("Skia failed to create the brush shader.");
    }

    private SKPathEffect GetDashEffect(in CoreStrokeDash dash)
    {
        if (dashCache.TryGet(dash, out var cached))
        {
            return cached;
        }

        var created = SKPathEffect.CreateDash(dash.Intervals.ToArray(), dash.Phase)
            ?? throw new InvalidOperationException("Skia failed to create the dash path effect.");
        dashCache.Add(dash, created);
        return created;
    }

    private void StampColor(UtilsColor color, SKBlendMode blendMode)
    {
        nativePaint.Reset();
        nativePaint.BlendMode = blendMode;
        nativePaint.SetColor(new SKColorF(color.R, color.G, color.B, color.A), srgbColorSpace);
    }

    private void LowerPath(CorePathBuilder path)
    {
        nativePath.Rewind();
        nativePath.FillType = path.FillRule switch
        {
            CoreFillRule.NonZero => SKPathFillType.Winding,
            CoreFillRule.EvenOdd => SKPathFillType.EvenOdd,
            _ => throw new ArgumentOutOfRangeException(nameof(path), "The path fill rule is not supported."),
        };

        foreach (var command in path.Commands)
        {
            switch (command.Verb)
            {
                case PathVerb.Move:
                    nativePath.MoveTo(command.Point1.X, command.Point1.Y);
                    break;
                case PathVerb.Line:
                    nativePath.LineTo(command.Point1.X, command.Point1.Y);
                    break;
                case PathVerb.Quadratic:
                    nativePath.QuadTo(
                        command.Point1.X,
                        command.Point1.Y,
                        command.Point2.X,
                        command.Point2.Y);
                    break;
                case PathVerb.Cubic:
                    nativePath.CubicTo(
                        command.Point1.X,
                        command.Point1.Y,
                        command.Point2.X,
                        command.Point2.Y,
                        command.Point3.X,
                        command.Point3.Y);
                    break;
                case PathVerb.Close:
                    nativePath.Close();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path), "The path contains an unsupported command.");
            }
        }
    }

    private void EnsureStateCapacity()
    {
        if (stateDepth < stateStack.Length)
        {
            return;
        }

        Array.Resize(ref stateStack, checked(stateStack.Length * 2));
    }

    private void EnsureBracketCapacity()
    {
        if (engineBracketDepth < bracketStack.Length)
        {
            return;
        }

        Array.Resize(ref bracketStack, checked(bracketStack.Length * 2));
    }

    private void CommitPush(StateKind state) => stateStack[stateDepth++] = state;

    private void Pop(StateKind expected, string name)
    {
        EnsureActive();
        if (stateDepth == 0)
        {
            throw new InvalidOperationException($"Cannot pop {name} state because the context state stack is empty.");
        }
        if (stateStack[stateDepth - 1] != expected)
        {
            throw new InvalidOperationException(
                $"Cannot pop {name} state before the more recently pushed {Describe(stateStack[stateDepth - 1])} state.");
        }

        canvas.Restore();
        stateStack[--stateDepth] = StateKind.None;
    }

    /// <summary>
    /// Reinstalls the engine transform on the pass's own save slot, above the surface baseline and
    /// below every author push. The caller guarantees the author stack is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pass's device offset composes below the engine transform — row vectors, so a point is
    /// mapped to frame device pixels first and shifted onto this surface second. The offset is
    /// identity for a surface that is the frame, and composing with the identity is exact, so a
    /// full-frame pass installs precisely the matrix it was handed.
    /// </para>
    /// <para>
    /// The slot the transform lives in sits directly above the innermost open engine layer bracket,
    /// not above the surface baseline, so installing a per-node transform inside a per-layer group
    /// replaces only itself and leaves the group's clip and layer standing.
    /// </para>
    /// </remarks>
    private void InstallEngineTransform(in Matrix3x2 engineToDevice)
    {
        var nativeTransform = ToSkiaMatrix(engineToDevice * deviceOffset);
        if (canvas.SaveCount > engineSlotBaseline)
        {
            canvas.RestoreToCount(engineSlotBaseline);
        }

        canvas.Save();
        canvas.Concat(nativeTransform);
    }

    private void ResetToBaseline()
    {
        if (canvas.SaveCount > baseSaveCount)
        {
            canvas.RestoreToCount(baseSaveCount);
        }
        if (stateDepth > 0)
        {
            Array.Clear(stateStack, 0, stateDepth);
            stateDepth = 0;
        }
        if (engineBracketDepth > 0)
        {
            Array.Clear(bracketStack, 0, engineBracketDepth);
            engineBracketDepth = 0;
        }
        engineSlotBaseline = baseSaveCount;
        passActive = false;
    }

    private void EnsureActive()
    {
        EnsureNotDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("No render pass is active.");
        }
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void EnsureFiniteMatrix(Matrix3x2 matrix, string parameterName)
    {
        if (!float.IsFinite(matrix.M11) ||
            !float.IsFinite(matrix.M12) ||
            !float.IsFinite(matrix.M21) ||
            !float.IsFinite(matrix.M22) ||
            !float.IsFinite(matrix.M31) ||
            !float.IsFinite(matrix.M32))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Affine matrix components must be finite.");
        }
    }

    private static SKMatrix ToSkiaMatrix(Matrix3x2 matrix) => new()
    {
        ScaleX = matrix.M11,
        SkewX = matrix.M21,
        TransX = matrix.M31,
        SkewY = matrix.M12,
        ScaleY = matrix.M22,
        TransY = matrix.M32,
        Persp0 = 0f,
        Persp1 = 0f,
        Persp2 = 1f,
    };

    private static SKStrokeCap ToSkiaStrokeCap(CoreLineCap cap) => cap switch
    {
        CoreLineCap.Butt => SKStrokeCap.Butt,
        CoreLineCap.Round => SKStrokeCap.Round,
        CoreLineCap.Square => SKStrokeCap.Square,
        _ => throw new ArgumentOutOfRangeException(nameof(cap), "The line cap is not supported."),
    };

    private static SKStrokeJoin ToSkiaStrokeJoin(CoreLineJoin join) => join switch
    {
        CoreLineJoin.Miter => SKStrokeJoin.Miter,
        CoreLineJoin.Round => SKStrokeJoin.Round,
        CoreLineJoin.Bevel => SKStrokeJoin.Bevel,
        _ => throw new ArgumentOutOfRangeException(nameof(join), "The line join is not supported."),
    };

    private static SKShaderTileMode ToSkiaTileMode(CoreSpreadMode spread) => spread switch
    {
        CoreSpreadMode.Clamp => SKShaderTileMode.Clamp,
        CoreSpreadMode.Repeat => SKShaderTileMode.Repeat,
        CoreSpreadMode.Mirror => SKShaderTileMode.Mirror,
        _ => throw new ArgumentOutOfRangeException(nameof(spread), "The spread mode is not supported."),
    };

    /// <summary>Lowers one portable blend mode, shared with the compositor's merge paint.</summary>
    internal static SKBlendMode ToSkiaBlendMode(CoreBlendMode blendMode) => blendMode switch
    {
        CoreBlendMode.SrcOver => SKBlendMode.SrcOver,
        CoreBlendMode.Multiply => SKBlendMode.Multiply,
        CoreBlendMode.Screen => SKBlendMode.Screen,
        CoreBlendMode.Overlay => SKBlendMode.Overlay,
        CoreBlendMode.Darken => SKBlendMode.Darken,
        CoreBlendMode.Lighten => SKBlendMode.Lighten,
        CoreBlendMode.ColorDodge => SKBlendMode.ColorDodge,
        CoreBlendMode.ColorBurn => SKBlendMode.ColorBurn,
        CoreBlendMode.HardLight => SKBlendMode.HardLight,
        CoreBlendMode.SoftLight => SKBlendMode.SoftLight,
        CoreBlendMode.Difference => SKBlendMode.Difference,
        CoreBlendMode.Exclusion => SKBlendMode.Exclusion,
        CoreBlendMode.Plus => SKBlendMode.Plus,
        _ => throw new ArgumentOutOfRangeException(nameof(blendMode), "The blend mode is not supported."),
    };

    /// <summary>Names the unbalanced author state kinds, outermost push first.</summary>
    private string DescribeStack()
    {
        var builder = new StringBuilder();
        for (var index = 0; index < stateDepth; index++)
        {
            if (index != 0)
            {
                builder.Append(", ");
            }

            builder.Append(Describe(stateStack[index]));
        }

        return builder.ToString();
    }

    private static string Describe(StateKind state) => state switch
    {
        StateKind.Transform => "transform",
        StateKind.Clip => "clip",
        StateKind.Opacity => "opacity",
        _ => "unknown",
    };

    private static string DescribeBracket(BracketKind kind) => kind switch
    {
        BracketKind.Layer => "layer",
        BracketKind.Unit => "unit",
        _ => "unknown",
    };

    private static int SaveSlotsFor(BracketKind kind) => kind switch
    {
        BracketKind.Layer => SaveSlotsPerLayerBracket,
        BracketKind.Unit => SaveSlotsPerUnitBracket,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "The engine bracket kind is not supported."),
    };

    private enum StateKind : byte
    {
        None,
        Transform,
        Clip,
        Opacity,
    }

    /// <summary>
    /// The engine-owned bracket kinds, which share one save stack and therefore one nesting order.
    /// </summary>
    private enum BracketKind : byte
    {
        None,
        Layer,
        Unit,
    }
}
