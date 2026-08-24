using System.Numerics;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Core;

/// <summary>
/// Implements the Tier-2 convenience primitives once, in terms of Tier-1
/// <see cref="IDrawContext2D.DrawPath"/>, for every backend to inherit.
/// </summary>
/// <remarks>
/// <para>
/// ARCHITECTURE §7.2: circles, ellipses, rectangles, rounded rectangles, lines, polylines, and arcs
/// are <em>conveniences</em>, not primitives. Each one builds its geometry with
/// <see cref="PathBuilderShapeExtensions"/> and hands it to the backend's own
/// <see cref="IDrawContext2D.DrawPath"/> — one Tier-1 call per convenience call, no exceptions. A
/// backend that lowers a circle itself would be a second, subtly different geometry path: different
/// tangent points, different antialiasing, different pixels for the same author intent. Deriving
/// from this class is how a backend avoids owning that problem.
/// </para>
/// <para>
/// <strong>One convenience breaks that rule on purpose.</strong>
/// <see cref="DrawGradientPolyline"/> and <see cref="DrawGradientSpline"/> emit one stroked path
/// per segment, because a run whose color and width change along its length has no single
/// <see cref="Paint"/> to be drawn with. That is §7.3's portable batch default — "a loop over
/// Tier 1/2" — reached for early, at author level, so that nobody writes the loop by hand while the
/// batch tier is still M2. Their <c>DrawGradientSpline</c> remarks state the consequences at the
/// joins rather than leaving them to be discovered.
/// </para>
/// <para>
/// <strong>Overrides are allowed but deliberately unused.</strong> §7.2 permits a backend to
/// override any convenience for quality or speed — <c>SKCanvas.DrawCircle</c> is the example it
/// gives. <c>SkiaDrawContext2D</c> nonetheless takes the portable default for every
/// one of them, because the property that makes conveniences safe to reach for is that
/// <c>DrawCircle</c> and the equivalent explicit <see cref="PathBuilder"/> produce the same pixels.
/// A native oval is a different rasterization of the same intent, and the difference shows up as an
/// unexplainable seam where an author mixes the two. The virtual members stay virtual so a backend
/// that needs a native path — a vector exporter emitting <c>&lt;circle&gt;</c>, say — can still take
/// it deliberately.
/// </para>
/// <para>
/// <strong>The scratch path.</strong> These are per-frame calls in the hottest authoring path in the
/// engine, so a convenience must not allocate a <see cref="PathBuilder"/> per call. Each context
/// owns exactly one scratch builder, rented through <see cref="RentScratchPath()"/> and reset — not
/// reallocated — on release, so a warm scene pays nothing after the builder reaches its stable
/// capacity.
/// </para>
/// <para>
/// <strong>Re-entrancy.</strong> Two questions, two different answers.
/// </para>
/// <para>
/// <em>Can a convenience corrupt a path the author is building?</em> No, structurally. The scratch
/// builder is a private field, <see cref="RentScratchPath()"/> is protected, and the lease never
/// escapes the call that took it. An author's <see cref="PathBuilder"/> is an object they
/// constructed and hold; the context has no way to reach it and never touches it. There is no shared
/// mutable builder between the two, so a half-built author path cannot be disturbed by any number of
/// interleaved <c>DrawCircle</c> calls.
/// </para>
/// <para>
/// <em>Can the scratch builder be entered twice?</em> Only by backend code, and it fails loudly if it
/// is. A rented scratch is flagged, and a second rent throws
/// <see cref="InvalidOperationException"/> rather than quietly resetting the geometry the outer call
/// is still assembling. The realistic ways in are an override that calls another convenience after
/// starting its own build, and a <see cref="DrawPath"/> implementation that calls back into a
/// convenience while it is being handed the scratch; both are bugs, and both now name themselves at
/// the point of failure instead of producing a mangled shape three frames later. The lease releases
/// in a <c>finally</c>, so a throwing <see cref="DrawPath"/> leaves the scratch rentable rather than
/// permanently poisoned.
/// </para>
/// <para>
/// The flag is not a lock and does not make a context thread-safe. Draw contexts are single-threaded
/// by the phase contract (§3.5); the flag detects re-entrancy on the one thread that is allowed to be
/// there, and concurrent misuse remains out of contract.
/// </para>
/// </remarks>
public abstract class DrawContext2DBase : IDrawContext2D
{
    /// <summary>
    /// Commands the scratch builder reserves up front, so the common conveniences never grow it.
    /// </summary>
    /// <remarks>
    /// The largest fixed-size shape here is the rounded rectangle at ten commands, and the closed
    /// ellipse is six; thirty-two leaves room for a short polyline or a multi-quadrant arc before the
    /// first growth. A long polyline grows the builder once and keeps the capacity for every frame
    /// after, which is the whole point of reusing one builder rather than sizing this for the worst
    /// case.
    /// </remarks>
    private const int InitialScratchCapacity = 32;

    private readonly PathBuilder scratchPath = new(FillRule.NonZero, InitialScratchCapacity);
    private bool scratchRented;

    /// <summary>Initializes the shared scratch geometry every convenience builds into.</summary>
    protected DrawContext2DBase()
    {
    }

    /// <inheritdoc />
    public abstract SurfaceSpec SurfaceSpec { get; }

    /// <inheritdoc />
    public abstract RenderCaps Caps { get; }

    /// <inheritdoc />
    public abstract float RenderScale { get; }

    /// <inheritdoc />
    public abstract float Scale { get; }

    /// <inheritdoc />
    public abstract void Clear(Color color);

    /// <inheritdoc />
    public abstract void DrawPath(PathBuilder path, in Paint paint);

    /// <inheritdoc />
    /// <remarks>
    /// Abstract, and the only Tier-1 member with no portable default that could have been written
    /// here even in principle: Core owns no glyph rasterizer, and ARCHITECTURE §12.1 forbids a
    /// second text pipeline existing to supply one.
    /// </remarks>
    public abstract void DrawText(ITextLayout layout, Color? colorOverride = null);

    /// <inheritdoc />
    public abstract void DrawImage(
        IImage image,
        in Matrix3x2 imageToLocal,
        ImageSampling sampling = ImageSampling.Linear);

    /// <inheritdoc />
    public abstract void SetEngineTransform(in Matrix3x2 engineToDevice);

    /// <inheritdoc />
    public abstract void BeginLayerBracket(in LayerBracket bracket);

    /// <inheritdoc />
    public abstract void EndLayerBracket();

    /// <inheritdoc />
    public abstract void BeginUnitBracket(in UnitBracket bracket);

    /// <inheritdoc />
    public abstract void EndUnitBracket();

    /// <inheritdoc />
    public abstract void BeginClipBracket(in ClipBracket bracket);

    /// <inheritdoc />
    public abstract void EndClipBracket();

    /// <inheritdoc />
    public abstract void PushTransform(in Matrix3x2 localTransform);

    /// <inheritdoc />
    public abstract void PopTransform();

    /// <inheritdoc />
    public abstract void PushClip(in Rect bounds);

    /// <inheritdoc />
    public abstract void PushClip(PathBuilder path);

    /// <inheritdoc />
    public abstract void PopClip();

    /// <inheritdoc />
    public abstract void PushOpacity(float opacity);

    /// <inheritdoc />
    public abstract void PopOpacity();

    /// <inheritdoc />
    public virtual void DrawCircle(in Vector2 center, float radius, in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddCircle(center, radius);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    public virtual void DrawEllipse(in Vector2 center, in Vector2 radii, in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddEllipse(center, radii);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    public virtual void DrawRect(in Rect bounds, in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddRect(bounds);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    public virtual void DrawRoundRect(in Rect bounds, in Vector2 cornerRadii, in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddRoundRect(bounds, cornerRadii);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    public virtual void DrawLine(in Vector2 start, in Vector2 end, in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddLine(start, end);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    public virtual void DrawPolyline(ReadOnlySpan<Vector2> points, in Paint paint, bool close = false)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddPolyline(points, close);
        DrawPath(scratch.Path, paint);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// A ramp is non-empty and its length is not <c>points.Length</c>, or
    /// <paramref name="vertexColors"/> is non-empty while <paramref name="template"/> carries a
    /// brush.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A width is not finite and nonnegative, or the ramp is empty and the template's stroke width
    /// is not positive.
    /// </exception>
    public virtual void DrawGradientPolyline(
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template,
        bool close = false)
    {
        var segmentCount = points.Length < 2 ? 0 : close ? points.Length : points.Length - 1;
        if (segmentCount == 0)
        {
            return;
        }

        RequireRamps(points.Length, vertexColors, vertexWidths, template);

        using var scratch = RentScratchPath();
        for (var index = 0; index < segmentCount; index++)
        {
            var next = index + 1 == points.Length ? 0 : index + 1;
            var paint = SegmentPaint(
                index,
                index,
                next,
                segmentCount,
                close,
                vertexColors,
                vertexWidths,
                template);
            if (paint is not { } stroke)
            {
                continue;
            }

            scratch.Path.Reset();
            scratch.Path.MoveTo(points[index].X, points[index].Y);
            scratch.Path.LineTo(points[next].X, points[next].Y);
            DrawPath(scratch.Path, stroke);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// A ramp is non-empty and its length is not the spline's control-point count, or
    /// <paramref name="vertexColors"/> is non-empty while <paramref name="template"/> carries a
    /// brush.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A width is not finite and nonnegative, or the ramp is empty and the template's stroke width
    /// is not positive.
    /// </exception>
    public virtual void DrawGradientSpline(
        in CatmullRomSegments spline,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template)
    {
        var segmentCount = spline.Count;
        if (segmentCount == 0)
        {
            return;
        }

        var points = spline.Points;
        RequireRamps(points.Length, vertexColors, vertexWidths, template);

        using var scratch = RentScratchPath();
        for (var index = 0; index < segmentCount; index++)
        {
            var next = index + 1 == points.Length ? 0 : index + 1;
            var paint = SegmentPaint(
                index,
                index,
                next,
                segmentCount,
                spline.IsClosed,
                vertexColors,
                vertexWidths,
                template);
            if (paint is not { } stroke)
            {
                continue;
            }

            var segment = spline[index];
            scratch.Path.Reset();
            scratch.Path.MoveTo(segment.Start.X, segment.Start.Y);
            scratch.Path.CubicTo(
                segment.Control1.X,
                segment.Control1.Y,
                segment.Control2.X,
                segment.Control2.Y,
                segment.End.X,
                segment.End.Y);
            DrawPath(scratch.Path, stroke);
        }
    }

    /// <inheritdoc />
    public virtual void DrawArc(
        in Vector2 center,
        in Vector2 radii,
        Angle startAngle,
        Angle sweepAngle,
        ArcMode mode,
        in Paint paint)
    {
        using var scratch = RentScratchPath();
        scratch.Path.AddArc(center, radii, startAngle, sweepAngle, mode);
        DrawPath(scratch.Path, paint);
    }

    /// <summary>Validates a gradient run's ramps against its vertex count and its template.</summary>
    /// <remarks>
    /// Everything checkable is checked once, before the first segment is drawn, so a mismatched ramp
    /// cannot leave half a run on the surface. Widths are validated here rather than left to
    /// <see cref="Paint"/>'s own guard because <see cref="Paint.Stroke(Color, float, bool, BlendMode, LineCap, LineJoin, float, StrokeDash?)"/>
    /// would name <c>width</c> on a value the caller never passed.
    /// </remarks>
    private static void RequireRamps(
        int vertexCount,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template)
    {
        if (!vertexColors.IsEmpty && vertexColors.Length != vertexCount)
        {
            throw new ArgumentException(
                $"A color ramp must carry one color per vertex: {vertexCount} expected, "
                + $"{vertexColors.Length} given.",
                nameof(vertexColors));
        }
        if (!vertexColors.IsEmpty && template.Brush is not null)
        {
            throw new ArgumentException(
                "A per-vertex color ramp replaces the paint's color source, so the template must "
                + "not carry a brush. Drop the ramp to stroke the whole run with the brush, or drop "
                + "the brush to ramp the color.",
                nameof(template));
        }
        if (!vertexWidths.IsEmpty && vertexWidths.Length != vertexCount)
        {
            throw new ArgumentException(
                $"A width ramp must carry one width per vertex: {vertexCount} expected, "
                + $"{vertexWidths.Length} given.",
                nameof(vertexWidths));
        }

        if (vertexWidths.IsEmpty)
        {
            if (template.StrokeWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(template),
                    template.StrokeWidth,
                    "Without a width ramp the template's stroke width paints the whole run, so it "
                    + "must be positive. default(Paint) and Paint.Fill carry no usable width.");
            }

            return;
        }

        for (var index = 0; index < vertexWidths.Length; index++)
        {
            if (!float.IsFinite(vertexWidths[index]) || vertexWidths[index] < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(vertexWidths),
                    vertexWidths[index],
                    "A ramped stroke width must be finite and nonnegative. Zero is allowed and "
                    + "paints nothing, which is how a taper reaches a point.");
            }
        }
    }

    /// <summary>
    /// Resolves the paint for one segment of a gradient run, or null when it paints nothing.
    /// </summary>
    /// <remarks>
    /// The cap is the caller's only on an open run's two terminal segments, which are the only ones
    /// with an end of the run to cap; every interior segment is <see cref="LineCap.Butt"/> so that
    /// neighbouring strokes abut instead of compositing twice over a shared cap. A closed run has no
    /// ends and is butt throughout. A terminal segment carries the caller's cap at its interior end
    /// as well, because a paint caps both ends of the path it is on — the one place a non-butt cap
    /// still overlaps, and the reason butt is the default worth keeping.
    /// </remarks>
    private static Paint? SegmentPaint(
        int segmentIndex,
        int start,
        int end,
        int segmentCount,
        bool close,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template)
    {
        var width = vertexWidths.IsEmpty
            ? template.StrokeWidth
            : (vertexWidths[start] + vertexWidths[end]) * 0.5f;
        if (width <= 0f)
        {
            return null;
        }

        var cap = !close && (segmentIndex == 0 || segmentIndex == segmentCount - 1)
            ? template.Cap
            : LineCap.Butt;

        if (vertexColors.IsEmpty)
        {
            return template.Brush is { } brush
                ? Paint.Stroke(
                    brush,
                    width,
                    template.IsAntialias,
                    template.BlendMode,
                    cap,
                    template.Join,
                    template.MiterLimit,
                    template.Dash)
                : Paint.Stroke(
                    template.Color,
                    width,
                    template.IsAntialias,
                    template.BlendMode,
                    cap,
                    template.Join,
                    template.MiterLimit,
                    template.Dash);
        }

        return Paint.Stroke(
            Midpoint(vertexColors[start], vertexColors[end]),
            width,
            template.IsAntialias,
            template.BlendMode,
            cap,
            template.Join,
            template.MiterLimit,
            template.Dash);
    }

    /// <summary>Averages two ramp colors the way a segment between them should read.</summary>
    /// <remarks>
    /// The mean is taken on premultiplied channels and then unpremultiplied, which is the only
    /// reading that keeps a fade toward transparency from dragging the hue toward whatever the
    /// invisible endpoint's channels happen to hold. Two colors of equal alpha reduce to the plain
    /// component mean, so the common all-one-hue trail is unaffected. When both alphas are zero the
    /// segment is invisible and the straight mean is returned rather than dividing by it.
    /// </remarks>
    private static Color Midpoint(in Color start, in Color end)
    {
        var alpha = (start.A + end.A) * 0.5f;
        if (alpha <= 0f)
        {
            return new Color(
                (start.R + end.R) * 0.5f,
                (start.G + end.G) * 0.5f,
                (start.B + end.B) * 0.5f,
                0f);
        }

        var scale = 0.5f / alpha;
        return new Color(
            ((start.R * start.A) + (end.R * end.A)) * scale,
            ((start.G * start.A) + (end.G * end.A)) * scale,
            ((start.B * start.A) + (end.B * end.A)) * scale,
            alpha);
    }

    /// <summary>Rents the context's scratch geometry with <see cref="FillRule.NonZero"/>.</summary>
    /// <returns>A lease that must be released before the next rent, normally with <c>using</c>.</returns>
    /// <exception cref="InvalidOperationException">The scratch path is already rented.</exception>
    protected ScratchPathLease RentScratchPath() => RentScratchPath(FillRule.NonZero);

    /// <summary>Rents the context's scratch geometry, empty and carrying the given fill rule.</summary>
    /// <param name="fillRule">The fill rule the rented builder reports.</param>
    /// <returns>A lease that must be released before the next rent, normally with <c>using</c>.</returns>
    /// <remarks>
    /// The builder is emptied on rent and again on release, so neither the previous tenant's commands
    /// nor this one's survive, while the underlying command array — and therefore the absence of
    /// allocation — does. The fill rule is written only when it actually changes, because
    /// <see cref="PathBuilder.FillRule"/>'s setter validates the enum and the conveniences all want
    /// the same rule.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The scratch path is already rented.</exception>
    /// <exception cref="ArgumentException"><paramref name="fillRule"/> is not defined.</exception>
    protected ScratchPathLease RentScratchPath(FillRule fillRule)
    {
        if (scratchRented)
        {
            throw new InvalidOperationException(
                "The draw context's scratch path is already rented. A Tier-2 convenience re-entered "
                + "itself — most likely a DrawPath implementation or a convenience override called "
                + "back into another convenience — and completing the inner call would discard the "
                + "geometry the outer one is still assembling.");
        }

        scratchPath.Reset();
        if (scratchPath.FillRule != fillRule)
        {
            scratchPath.FillRule = fillRule;
        }

        scratchRented = true;
        return new ScratchPathLease(this);
    }

    /// <summary>Exclusive, non-escaping access to a draw context's one scratch path builder.</summary>
    /// <remarks>
    /// A <c>ref struct</c> so that the lease itself cannot be boxed, stored in a field, or captured
    /// by a lambda: the borrow ends where the <c>using</c> ends, which is the property the
    /// re-entrancy guard depends on. <see cref="Path"/> is still an ordinary object reference and
    /// could in principle be stashed by a misbehaving backend; the contract is that it must not be,
    /// exactly as <see cref="IDrawContext2D.DrawPath"/>'s argument must not be retained.
    /// </remarks>
    protected readonly ref struct ScratchPathLease
    {
        private readonly DrawContext2DBase context;

        /// <summary>Takes the lease. Callers go through <see cref="RentScratchPath()"/>.</summary>
        /// <param name="context">The context whose scratch path is being borrowed.</param>
        internal ScratchPathLease(DrawContext2DBase context) => this.context = context;

        /// <summary>Gets the borrowed builder, emptied and ready to append to.</summary>
        public PathBuilder Path => context.scratchPath;

        /// <summary>Releases the borrow and empties the builder, keeping its capacity.</summary>
        public void Dispose()
        {
            context.scratchPath.Reset();
            context.scratchRented = false;
        }
    }
}
