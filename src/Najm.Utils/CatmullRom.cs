using System.Numerics;
using System.Runtime.CompilerServices;

namespace Najm.Utils;

/// <summary>One cubic Bézier segment, stored as its four control points.</summary>
/// <remarks>
/// This is the exchange format between curve maths and a path sink: every spline this assembly
/// produces is a sequence of these, so a backend flattens and antialiases them at whatever
/// resolution the render scale asks for instead of consuming a polyline sampled at one fixed
/// density.
/// </remarks>
public readonly struct CubicSegment : IEquatable<CubicSegment>
{
    /// <summary>Creates a cubic Bézier segment from four finite control points.</summary>
    /// <param name="start">The on-curve point at <c>t = 0</c>.</param>
    /// <param name="control1">The off-curve control point governing the departure tangent.</param>
    /// <param name="control2">The off-curve control point governing the arrival tangent.</param>
    /// <param name="end">The on-curve point at <c>t = 1</c>.</param>
    public CubicSegment(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end)
    {
        RequireFinite(start, nameof(start));
        RequireFinite(control1, nameof(control1));
        RequireFinite(control2, nameof(control2));
        RequireFinite(end, nameof(end));

        Start = start;
        Control1 = control1;
        Control2 = control2;
        End = end;
    }

    /// <summary>Gets the on-curve point at <c>t = 0</c>.</summary>
    public Vector2 Start { get; }

    /// <summary>Gets the off-curve control point governing the departure tangent.</summary>
    public Vector2 Control1 { get; }

    /// <summary>Gets the off-curve control point governing the arrival tangent.</summary>
    public Vector2 Control2 { get; }

    /// <summary>Gets the on-curve point at <c>t = 1</c>.</summary>
    public Vector2 End { get; }

    /// <summary>Evaluates the segment at a finite parameter.</summary>
    /// <param name="t">
    /// The finite curve parameter. Values outside [0, 1] are not clamped: the cubic is evaluated
    /// as written, which extrapolates.
    /// </param>
    /// <returns>The point on the cubic at <paramref name="t"/>.</returns>
    public Vector2 Evaluate(float t)
    {
        RequireFinite(t, nameof(t));

        var u = 1f - t;
        return (u * u * u * Start)
            + (3f * u * u * t * Control1)
            + (3f * u * t * t * Control2)
            + (t * t * t * End);
    }

    /// <summary>Evaluates the first derivative with respect to the segment parameter.</summary>
    /// <param name="t">The finite curve parameter; see <see cref="Evaluate"/> for extrapolation.</param>
    /// <returns>
    /// The unnormalized velocity. Its magnitude depends on how the segment was parameterized, so
    /// compare directions rather than vectors when joining two segments. A zero vector marks a
    /// cusp.
    /// </returns>
    public Vector2 Tangent(float t)
    {
        RequireFinite(t, nameof(t));

        var u = 1f - t;
        return (3f * u * u * (Control1 - Start))
            + (6f * u * t * (Control2 - Control1))
            + (3f * t * t * (End - Control2));
    }

    /// <inheritdoc />
    public bool Equals(CubicSegment other) =>
        Start.Equals(other.Start)
        && Control1.Equals(other.Control1)
        && Control2.Equals(other.Control2)
        && End.Equals(other.End);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CubicSegment other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Control1, Control2, End);

    /// <summary>Tests two segments for exact control-point equality.</summary>
    public static bool operator ==(CubicSegment left, CubicSegment right) => left.Equals(right);

    /// <summary>Tests two segments for exact control-point inequality.</summary>
    public static bool operator !=(CubicSegment left, CubicSegment right) => !left.Equals(right);

    private static void RequireFinite(Vector2 point, string parameterName)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                point,
                "A curve control point must be finite.");
        }
    }

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A curve parameter must be finite.");
        }
    }
}

/// <summary>
/// Converts a sequence of control points into the cubic Bézier segments of the Catmull-Rom spline
/// that interpolates them.
/// </summary>
/// <remarks>
/// <para>
/// A Catmull-Rom segment is a cubic, so the conversion is exact rather than a sampling: nothing
/// here produces a polyline. Emit the segments into a path and let the backend flatten them, and
/// one path is correct at draft resolution and at 4K without being rebuilt.
/// </para>
/// <para>
/// <strong>Why centripetal is the default.</strong> The alpha family sets how much of the chord
/// length enters the knot spacing: <see cref="UniformAlpha"/> ignores it, <see cref="ChordalAlpha"/>
/// uses it in full, and <see cref="CentripetalAlpha"/> uses its square root. Uniform is the cheap
/// choice and the wrong default, because when consecutive points are unevenly spaced it gives a
/// short segment the tangent magnitude its long neighbour deserves, and the curve overshoots, loops,
/// and cusps. Sampled simulation output — a phase-space trajectory that crawls then sprints, a trail
/// behind an accelerating body — is exactly that kind of data. Centripetal is proved never to
/// self-intersect or cusp within a segment, and unlike chordal it does not slacken tangents into
/// wide bulges around distant neighbours. Pass a different alpha deliberately; do not inherit one.
/// </para>
/// </remarks>
public static class CatmullRom
{
    /// <summary>
    /// The alpha that ignores chord length. Cusps and self-intersects on unevenly spaced points;
    /// see the type remarks before choosing it.
    /// </summary>
    public const float UniformAlpha = 0f;

    /// <summary>
    /// The alpha that takes the square root of chord length. The default, and the only member of
    /// the family that cannot cusp or self-intersect inside a segment.
    /// </summary>
    public const float CentripetalAlpha = 0.5f;

    /// <summary>
    /// The alpha that takes chord length in full. Tangents follow the polygon most closely but bulge
    /// widest around a distant neighbour.
    /// </summary>
    public const float ChordalAlpha = 1f;

    /// <summary>
    /// Describes the spline that starts at the first control point and ends at the last one.
    /// </summary>
    /// <param name="points">
    /// The control points, in order. Fewer than two yields no segments; the span is not copied, so
    /// it must stay valid while the result is used.
    /// </param>
    /// <param name="alpha">The parameterization exponent in [0, 1]; see the type remarks.</param>
    /// <returns>
    /// The <c>points.Length - 1</c> cubic segments between consecutive points, or none when there
    /// are fewer than two points.
    /// </returns>
    /// <remarks>
    /// The two end segments have no real neighbour on the outside, so a phantom point is reflected
    /// through each end — <c>2 * first - second</c> and <c>2 * last - penultimate</c>. The curve
    /// therefore begins at the first point and finishes at the last one rather than dropping them.
    /// </remarks>
    public static CatmullRomSegments Open(
        ReadOnlySpan<Vector2> points,
        float alpha = CentripetalAlpha) =>
        new(points, alpha, closed: false);

    /// <summary>Describes the spline that wraps from the last control point back to the first.</summary>
    /// <param name="points">
    /// The control points, in order, with no repeat of the first point at the end. The span is not
    /// copied, so it must stay valid while the result is used.
    /// </param>
    /// <param name="alpha">The parameterization exponent in [0, 1]; see the type remarks.</param>
    /// <returns>
    /// The <c>points.Length</c> cubic segments of the loop, the last of which returns to the first
    /// point, or none when there are fewer than two points.
    /// </returns>
    /// <remarks>
    /// Neighbours wrap, so no phantom points are involved and the joint at the first point is as
    /// smooth as every other joint. Two points describe a degenerate loop that runs out along the
    /// chord and straight back.
    /// </remarks>
    public static CatmullRomSegments Closed(
        ReadOnlySpan<Vector2> points,
        float alpha = CentripetalAlpha) =>
        new(points, alpha, closed: true);

    /// <summary>
    /// Converts one Catmull-Rom segment — the span from <paramref name="start"/> to
    /// <paramref name="end"/>, shaped by the point on either side — into the cubic Bézier that
    /// traces it exactly.
    /// </summary>
    /// <param name="before">The control point preceding <paramref name="start"/>.</param>
    /// <param name="start">The point the segment leaves, which the cubic interpolates at <c>t = 0</c>.</param>
    /// <param name="end">The point the segment reaches, which the cubic interpolates at <c>t = 1</c>.</param>
    /// <param name="after">The control point following <paramref name="end"/>.</param>
    /// <param name="alpha">The parameterization exponent in [0, 1]; see the type remarks.</param>
    /// <returns>The equivalent cubic Bézier segment.</returns>
    /// <remarks>
    /// Coincident points are the classic division by zero in this formula and are handled rather
    /// than propagated. A zero-length segment (<paramref name="start"/> equal to
    /// <paramref name="end"/>) collapses to a degenerate cubic that stays put instead of inventing
    /// a tangent, and a neighbour coincident with its endpoint borrows the segment's own knot
    /// spacing, which locally reduces the tangent to the uniform one.
    /// </remarks>
    public static CubicSegment ToCubic(
        in Vector2 before,
        in Vector2 start,
        in Vector2 end,
        in Vector2 after,
        float alpha = CentripetalAlpha)
    {
        RequireAlpha(alpha);
        RequireFinite(before, nameof(before));
        RequireFinite(start, nameof(start));
        RequireFinite(end, nameof(end));
        RequireFinite(after, nameof(after));

        return ToCubicCore(before, start, end, after, alpha);
    }

    /// <summary>
    /// Builds the segment from points and an alpha the caller has already validated.
    /// </summary>
    internal static CubicSegment ToCubicCore(
        Vector2 before,
        Vector2 start,
        Vector2 end,
        Vector2 after,
        float alpha)
    {
        // A zero-length segment has no direction to interpolate. Emitting the degenerate cubic keeps
        // the caller's point count intact and draws nothing, where inventing tangents from the
        // neighbours would draw a small loop out of a segment that should not move at all.
        if (start == end)
        {
            return new CubicSegment(start, start, end, end);
        }

        // Barry-Goldman knot spacing: the parameter advances by the chord length raised to alpha.
        // The middle spacing is strictly positive because the segment has non-zero length; an outer
        // spacing can still be zero when a neighbour repeats its endpoint, and borrows the middle.
        var d1 = KnotSpacing(Vector2.Distance(before, start), alpha);
        var d2 = KnotSpacing(Vector2.Distance(start, end), alpha);
        var d3 = KnotSpacing(Vector2.Distance(end, after), alpha);
        if (d1 == 0f)
        {
            d1 = d2;
        }
        if (d3 == 0f)
        {
            d3 = d2;
        }

        // Non-uniform Catmull-Rom tangents, scaled from the knot parameter into this segment's own
        // [0, 1] parameter by the factor d2. With every spacing equal to one these reduce to the
        // familiar uniform tangents (end - before) / 2 and (after - start) / 2.
        var departure = (((start - before) / d1)
            - ((end - before) / (d1 + d2))
            + ((end - start) / d2)) * d2;
        var arrival = (((end - start) / d2)
            - ((after - start) / (d2 + d3))
            + ((after - end) / d3)) * d2;

        // Hermite to Bézier: the control points sit one third of each tangent from the endpoints.
        return new CubicSegment(
            start,
            start + (departure / 3f),
            end - (arrival / 3f),
            end);
    }

    /// <summary>Validates the parameterization exponent.</summary>
    internal static void RequireAlpha(float alpha)
    {
        if (!float.IsFinite(alpha) || alpha < 0f || alpha > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alpha),
                alpha,
                "A Catmull-Rom parameterization exponent must be finite and in [0, 1].");
        }
    }

    /// <summary>Validates one control point.</summary>
    internal static void RequireFinite(in Vector2 point, string parameterName)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                point,
                "A Catmull-Rom control point must be finite.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float KnotSpacing(float distance, float alpha) => alpha switch
    {
        // Uniform spacing is one even between coincident points, which is why alpha 0 never divides
        // by zero and never needs the borrowing above.
        UniformAlpha => 1f,
        CentripetalAlpha => MathF.Sqrt(distance),
        ChordalAlpha => distance,
        _ => MathF.Pow(distance, alpha),
    };
}

/// <summary>
/// The cubic Bézier segments of one Catmull-Rom spline, computed on demand from the caller's
/// control points.
/// </summary>
/// <remarks>
/// This is a view, not a container: it holds the caller's span and allocates nothing, and each
/// segment is derived when it is asked for. Obtain one from <see cref="CatmullRom.Open"/> or
/// <see cref="CatmullRom.Closed"/>.
/// </remarks>
public readonly ref struct CatmullRomSegments
{
    private readonly ReadOnlySpan<Vector2> _points;
    private readonly float _alpha;
    private readonly bool _closed;

    internal CatmullRomSegments(ReadOnlySpan<Vector2> points, float alpha, bool closed)
    {
        CatmullRom.RequireAlpha(alpha);
        for (var index = 0; index < points.Length; index++)
        {
            CatmullRom.RequireFinite(points[index], nameof(points));
        }

        _points = points;
        _alpha = alpha;
        _closed = closed;
    }

    /// <summary>Gets the number of cubic segments, which is zero for fewer than two points.</summary>
    public int Count =>
        _points.Length < 2
            ? 0
            : _closed ? _points.Length : _points.Length - 1;

    /// <summary>Gets whether the spline wraps back to its first control point.</summary>
    public bool IsClosed => _closed;

    /// <summary>Gets the parameterization exponent these segments were built with.</summary>
    public float Alpha => _alpha;

    /// <summary>Gets the control points the segments are derived from.</summary>
    public ReadOnlySpan<Vector2> Points => _points;

    /// <summary>Gets the cubic segment leaving control point <paramref name="index"/>.</summary>
    /// <param name="index">The segment index, from zero to <see cref="Count"/> exclusive.</param>
    /// <returns>The cubic Bézier tracing that span of the spline.</returns>
    public CubicSegment this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            var count = _points.Length;
            if (_closed)
            {
                return CatmullRom.ToCubicCore(
                    _points[(index + count - 1) % count],
                    _points[index],
                    _points[(index + 1) % count],
                    _points[(index + 2) % count],
                    _alpha);
            }

            var start = _points[index];
            var end = _points[index + 1];
            return CatmullRom.ToCubicCore(
                index > 0 ? _points[index - 1] : Reflect(start, end),
                start,
                end,
                index + 2 < count ? _points[index + 2] : Reflect(end, start),
                _alpha);
        }
    }

    /// <summary>Gets an enumerator over the segments, in order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Reflects <paramref name="other"/> through <paramref name="anchor"/>.</summary>
    private static Vector2 Reflect(Vector2 anchor, Vector2 other) => (anchor * 2f) - other;

    /// <summary>Walks the segments of a <see cref="CatmullRomSegments"/> without allocating.</summary>
    public ref struct Enumerator
    {
        private readonly CatmullRomSegments _segments;
        private int _index;

        internal Enumerator(CatmullRomSegments segments)
        {
            _segments = segments;
            _index = -1;
            Current = default;
        }

        /// <summary>Gets the segment the enumerator is positioned on.</summary>
        public CubicSegment Current { get; private set; }

        /// <summary>Advances to the next segment.</summary>
        /// <returns><see langword="true"/> if a segment was available.</returns>
        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _segments.Count)
            {
                return false;
            }

            _index = next;
            Current = _segments[next];
            return true;
        }
    }
}
