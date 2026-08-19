using System.Numerics;
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
