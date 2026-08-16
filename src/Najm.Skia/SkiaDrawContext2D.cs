using System.Numerics;
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
/// A <see cref="SkiaRenderTarget"/> owns and reuses this object. Authors receive it as a borrowed
/// <see cref="IDrawContext2D"/> and must not retain it after the target is disposed. One native
/// scratch path and paint are backend-owned, rewound or reset, and reused for every draw. Rewinding
/// the path retains its native storage for allocation-free drawing at a stable command count.
/// State pushes share one strict typed LIFO stack and are balanced automatically when the owning
/// target is disposed. The native objects Skia forces us to allocate for brushes and dashes live in
/// context-owned caches keyed by the portable descriptor <em>value</em>: the first appearance of a
/// gradient or dash allocates, and every repetition is a dictionary hit that allocates nothing.
/// </remarks>
public sealed class SkiaDrawContext2D : IDrawContext2D
{
    private const int InitialStateCapacity = 16;

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
    private readonly Dictionary<CoreBrush, SKShader> shaderCache = [];
    private readonly Dictionary<CoreStrokeDash, SKPathEffect> dashCache = [];
    private StateKind[] stateStack = new StateKind[InitialStateCapacity];
    private RenderCaps caps;
    private float renderScale;
    private int stateDepth;
    private bool passActive;
    private bool disposed;

    internal SkiaDrawContext2D(SKCanvas canvas, SurfaceSpec surfaceSpec)
    {
        this.canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        baseSaveCount = canvas.SaveCount;
        SurfaceSpec = surfaceSpec;
    }

    /// <inheritdoc />
    public SurfaceSpec SurfaceSpec { get; }

    /// <inheritdoc />
    public RenderCaps Caps
    {
        get
        {
            EnsureActive();
            return caps;
        }
    }

    /// <inheritdoc />
    public float RenderScale
    {
        get
        {
            EnsureActive();
            return renderScale;
        }
    }

    /// <inheritdoc />
    public float Scale
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
    public void Clear(UtilsColor color)
    {
        EnsureActive();
        StampColor(color, SKBlendMode.Src);
        canvas.DrawPaint(nativePaint);
    }

    /// <inheritdoc />
    public void DrawPath(CorePathBuilder path, in CorePaint paint)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(path);

        LowerPath(path);
        StampPathPaint(paint);
        canvas.DrawPath(nativePath, nativePaint);
    }

    /// <inheritdoc />
    public void DrawImage(
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
    public void PushTransform(in Matrix3x2 localTransform)
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
    public void PopTransform() => Pop(StateKind.Transform, "transform");

    /// <inheritdoc />
    public void PushClip(in CoreRect bounds)
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
    public void PushClip(CorePathBuilder path)
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
    public void PopClip() => Pop(StateKind.Clip, "clip");

    /// <inheritdoc />
    public void PushOpacity(float opacity)
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
    public void PopOpacity() => Pop(StateKind.Opacity, "opacity");

    internal void BeginPass(
        float renderScale,
        RenderCaps caps,
        in Matrix3x2 engineBaseTransform)
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
        var nativeBaseTransform = ToSkiaMatrix(engineBaseTransform);

        ResetToBaseline();
        var saveCount = canvas.SaveCount;
        try
        {
            canvas.Save();
            canvas.Concat(nativeBaseTransform);
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
        ResetToBaseline();
        if (unbalancedStateCount != 0)
        {
            throw new InvalidOperationException(
                $"The render pass ended with {unbalancedStateCount} unbalanced context state push(es); baseline state was restored.");
        }
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
            DisposeCache(shaderCache);
            DisposeCache(dashCache);
            srgbColorSpace.Dispose();
        }
    }

    /// <summary>Gets how many brush values currently hold a cached native shader.</summary>
    internal int CachedShaderCount => shaderCache.Count;

    /// <summary>Gets how many dash values currently hold a cached native path effect.</summary>
    internal int CachedDashCount => dashCache.Count;

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
        if (shaderCache.TryGetValue(brush, out var cached))
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
        if (dashCache.TryGetValue(dash, out var cached))
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

    private static void DisposeCache<TKey, TValue>(Dictionary<TKey, TValue> cache)
        where TKey : notnull
        where TValue : IDisposable
    {
        foreach (var entry in cache)
        {
            entry.Value.Dispose();
        }

        cache.Clear();
    }

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

    private static SKBlendMode ToSkiaBlendMode(CoreBlendMode blendMode) => blendMode switch
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

    private static string Describe(StateKind state) => state switch
    {
        StateKind.Transform => "transform",
        StateKind.Clip => "clip",
        StateKind.Opacity => "opacity",
        _ => "unknown",
    };

    private enum StateKind : byte
    {
        None,
        Transform,
        Clip,
        Opacity,
    }
}
