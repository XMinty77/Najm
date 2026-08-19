using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Appends the standard rounded and rectilinear shapes to a <see cref="PathBuilder"/>.</summary>
/// <remarks>
/// <para>
/// This is the single geometry implementation behind the Tier-2 conveniences of
/// <see cref="DrawContext2DBase"/> (ARCHITECTURE §7.2). It lives on <see cref="PathBuilder"/> rather
/// than only inside the context because a convenience can only draw a shape on its own, while an
/// author who needs a ring, a keyhole, or a rounded rectangle with a circular bite out of it needs
/// the same curves <em>appended to a larger path</em>. Exposing the geometry keeps those two uses on
/// one set of control points instead of two subtly different ones.
/// </para>
/// <para>
/// <strong>Curves are cubics, never sampled polylines.</strong> Every rounded corner is the
/// classical circular-arc Bézier approximation, split so that no single cubic spans more than a
/// quarter turn, and the backend flattens the result at the resolution the render scale asks for.
/// See <see cref="QuarterTurnKappa"/> for the constant and its error.
/// </para>
/// <para>
/// <strong>Angle convention.</strong> Arcs are parameterized as
/// <c>center + (radii.X·cos θ, radii.Y·sin θ)</c>, so θ = 0 is the point at
/// <c>center + (radii.X, 0)</c> and a positive sweep turns from +x toward +y. Local coordinates
/// have y pointing down (§3.2), so a positive sweep reads as clockwise on screen.
/// </para>
/// <para>
/// <strong>Winding.</strong> Every closed shape here is emitted in the same direction — from the +x
/// side toward +y — so shapes composed into one path share a winding and
/// <see cref="FillRule.NonZero"/> unions them, while a contour reversed by the author cuts a hole.
/// </para>
/// <para>Nothing here allocates once the builder has reached a stable capacity.</para>
/// </remarks>
public static class PathBuilderShapeExtensions
{
    /// <summary>
    /// The control-point ratio that turns a quarter circle into one cubic Bézier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is <c>4/3 · tan(θ/4)</c> at <c>θ = π/2</c>, which is <c>4·(√2 − 1)/3</c> =
    /// 0.552284749830793…, the ratio that makes the cubic's own midpoint land exactly on the circle.
    /// It is a literal here so that it folds at compile time; <c>QuarterTurnKappaIsTheDerivedRatio</c>
    /// in the test suite recomputes it from <c>√2</c> and fails if the two ever disagree, and
    /// <c>QuarterTurnCubicStaysWithinTheKnownRadialError</c> pins the approximation error.
    /// </para>
    /// <para>
    /// A quarter turn is the widest sweep this approximation is used for: the maximum radial
    /// deviation is about 2.7 × 10⁻⁴ of the radius there, and it grows roughly as the sixth power of
    /// the sweep, so a half turn approximated with one cubic would be about sixty times worse and a
    /// full turn would not close. <see cref="AddArc"/> therefore splits by quadrant rather than
    /// widening the sweep.
    /// </para>
    /// </remarks>
    public const float QuarterTurnKappa = 0.5522847498307936f;

    /// <summary>The widest sweep, in radians, that one cubic segment is allowed to cover.</summary>
    private const double MaxSegmentSweep = Math.PI / 2d;

    /// <summary>Appends a closed circular contour.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radius">The finite nonnegative local-unit radius.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not finite and nonnegative, or a resulting coordinate is not
    /// finite.
    /// </exception>
    public static PathBuilder AddCircle(this PathBuilder path, in Vector2 center, float radius) =>
        path.AddEllipse(center, new Vector2(radius, radius));

    /// <summary>Appends a closed axis-aligned elliptical contour as four quarter-turn cubics.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radii">The finite nonnegative local-unit semi-axes.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// The contour starts at <c>center + (radii.X, 0)</c> and turns toward +y. Unlike
    /// <see cref="AddArc"/> this overload never evaluates a trigonometric function: the four
    /// quadrant endpoints are the exact axis points, so the extreme points of the ellipse are exact
    /// and the contour closes on the coordinate it opened with.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius is not finite and nonnegative, or a resulting coordinate is not finite.
    /// </exception>
    public static PathBuilder AddEllipse(this PathBuilder path, in Vector2 center, in Vector2 radii)
    {
        ArgumentNullException.ThrowIfNull(path);
        var radiusX = RequireRadius(radii.X, nameof(radii));
        var radiusY = RequireRadius(radii.Y, nameof(radii));
        var centerX = center.X;
        var centerY = center.Y;

        path.MoveTo(centerX + radiusX, centerY);
        AppendQuadrant(path, centerX, centerY, radiusX, radiusY, 1f, 0f, 0f, 1f);
        AppendQuadrant(path, centerX, centerY, radiusX, radiusY, 0f, 1f, -1f, 0f);
        AppendQuadrant(path, centerX, centerY, radiusX, radiusY, -1f, 0f, 0f, -1f);
        AppendQuadrant(path, centerX, centerY, radiusX, radiusY, 0f, -1f, 1f, 0f);
        return path.Close();
    }

    /// <summary>Appends a closed axis-aligned rectangular contour.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="bounds">The rectangle, in local units.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>The contour runs top-left, top-right, bottom-right, bottom-left, and closes.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static PathBuilder AddRect(this PathBuilder path, in Rect bounds)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path
            .MoveTo(bounds.Left, bounds.Top)
            .LineTo(bounds.Right, bounds.Top)
            .LineTo(bounds.Right, bounds.Bottom)
            .LineTo(bounds.Left, bounds.Bottom)
            .Close();
    }

    /// <summary>Appends a closed rectangular contour whose corners are elliptical quarter turns.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="bounds">The rectangle, in local units.</param>
    /// <param name="cornerRadii">
    /// The finite nonnegative local-unit corner semi-axes. Each component is clamped to half the
    /// corresponding side, so an over-large radius yields a stadium or an ellipse rather than
    /// self-intersecting corners.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// A corner radius that clamps to zero on either axis degenerates the corner entirely, so the
    /// result is exactly <see cref="AddRect"/> — the same four commands, not four cubics with
    /// coincident control points.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A corner radius is not finite and nonnegative.
    /// </exception>
    public static PathBuilder AddRoundRect(this PathBuilder path, in Rect bounds, in Vector2 cornerRadii)
    {
        ArgumentNullException.ThrowIfNull(path);
        var radiusX = Math.Min(RequireRadius(cornerRadii.X, nameof(cornerRadii)), bounds.Width * 0.5f);
        var radiusY = Math.Min(RequireRadius(cornerRadii.Y, nameof(cornerRadii)), bounds.Height * 0.5f);
        if (radiusX == 0f || radiusY == 0f)
        {
            return path.AddRect(bounds);
        }

        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;
        var offsetX = radiusX * QuarterTurnKappa;
        var offsetY = radiusY * QuarterTurnKappa;

        return path
            .MoveTo(left + radiusX, top)
            .LineTo(right - radiusX, top)
            .CubicTo(right - radiusX + offsetX, top, right, top + radiusY - offsetY, right, top + radiusY)
            .LineTo(right, bottom - radiusY)
            .CubicTo(right, bottom - radiusY + offsetY, right - radiusX + offsetX, bottom, right - radiusX, bottom)
            .LineTo(left + radiusX, bottom)
            .CubicTo(left + radiusX - offsetX, bottom, left, bottom - radiusY + offsetY, left, bottom - radiusY)
            .LineTo(left, top + radiusY)
            .CubicTo(left, top + radiusY - offsetY, left + radiusX - offsetX, top, left + radiusX, top)
            .Close();
    }

    /// <summary>Appends one open straight segment as its own contour.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="start">The finite local-unit start point.</param>
    /// <param name="end">The finite local-unit end point.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>The contour is left open, because a closed one would double back over itself.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static PathBuilder AddLine(this PathBuilder path, in Vector2 start, in Vector2 end)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.MoveTo(start.X, start.Y).LineTo(end.X, end.Y);
    }

    /// <summary>Appends a contour of straight segments through the given points.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="points">The finite local-unit vertices, in order; the span is not retained.</param>
    /// <param name="close">
    /// Whether to close the contour back to the first point. A closed polyline stroked with a
    /// non-butt cap shows a join at the seam rather than two caps, which is the reason to close it
    /// here rather than repeating the first point.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Fewer than two points describe no contour and the builder is left untouched — not even a
    /// <see cref="PathBuilder.MoveTo"/>, so a lone vertex cannot leave a stray open contour behind
    /// for a later <see cref="PathBuilder.Close"/> to pick up. This matches
    /// <see cref="PathBuilderSplineExtensions.AddOpenCatmullRom"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static PathBuilder AddPolyline(
        this PathBuilder path,
        ReadOnlySpan<Vector2> points,
        bool close = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (points.Length < 2)
        {
            return path;
        }

        path.MoveTo(points[0].X, points[0].Y);
        for (var index = 1; index < points.Length; index++)
        {
            path.LineTo(points[index].X, points[index].Y);
        }

        return close ? path.Close() : path;
    }

    /// <summary>Appends an elliptical arc, split so no cubic spans more than a quarter turn.</summary>
    /// <param name="path">The builder to append to.</param>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radii">The finite nonnegative local-unit semi-axes.</param>
    /// <param name="startAngle">Where the arc begins, measured from <c>center + (radii.X, 0)</c>.</param>
    /// <param name="sweepAngle">
    /// How far the arc turns. A positive sweep turns from +x toward +y, which is clockwise on
    /// screen. A sweep beyond a full turn is emitted as given and retraces the ellipse.
    /// </param>
    /// <param name="mode">How the arc's two ends are joined into a contour.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The number of cubics is <c>ceil(|sweep| / 90°)</c>, at least one, and the sweep is divided
    /// equally between them, so the per-segment error never exceeds the quarter-turn bound
    /// documented on <see cref="QuarterTurnKappa"/> and a 91° arc is two 45.5° cubics rather than one
    /// bad one.
    /// </para>
    /// <para>
    /// Unlike <see cref="AddEllipse"/>, endpoints here come from <see cref="Math.SinCos"/> and carry
    /// its rounding: a quarter-turn arc from 0° reaches <c>x = center.X + radii.X · 6.1e-17</c>
    /// rather than exactly <c>center.X</c>. That is far below single-precision resolution and cannot
    /// move a rasterized pixel, but it is why the closed-ellipse overload exists separately instead
    /// of forwarding here.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius is not finite and nonnegative, or a resulting coordinate is not finite.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="mode"/> is not defined.</exception>
    public static PathBuilder AddArc(
        this PathBuilder path,
        in Vector2 center,
        in Vector2 radii,
        Angle startAngle,
        Angle sweepAngle,
        ArcMode mode = ArcMode.Open)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException("The arc mode is not defined.", nameof(mode));
        }

        var radiusX = RequireRadius(radii.X, nameof(radii));
        var radiusY = RequireRadius(radii.Y, nameof(radii));
        var centerX = center.X;
        var centerY = center.Y;
        var start = startAngle.Radians;
        var sweep = sweepAngle.Radians;
        var segments = (int)Math.Max(1d, Math.Ceiling(Math.Abs(sweep) / MaxSegmentSweep));
        var step = sweep / segments;
        var ratio = (float)(4d / 3d * Math.Tan(step / 4d));

        var (sin0, cos0) = Math.SinCos(start);
        if (mode == ArcMode.Pie)
        {
            path.MoveTo(centerX, centerY);
            path.LineTo(
                centerX + (radiusX * (float)cos0),
                centerY + (radiusY * (float)sin0));
        }
        else
        {
            path.MoveTo(
                centerX + (radiusX * (float)cos0),
                centerY + (radiusY * (float)sin0));
        }

        for (var index = 1; index <= segments; index++)
        {
            var (sin1, cos1) = Math.SinCos(start + (step * index));
            AppendSegment(
                path,
                centerX,
                centerY,
                radiusX,
                radiusY,
                (float)cos0,
                (float)sin0,
                (float)cos1,
                (float)sin1,
                ratio);
            (sin0, cos0) = (sin1, cos1);
        }

        return mode == ArcMode.Open ? path : path.Close();
    }

    /// <summary>Appends one exact quadrant of an axis-aligned ellipse.</summary>
    /// <remarks>
    /// The unit vectors are the literals ±1 and 0, so <c>ratio · sin</c> and <c>ratio · cos</c>
    /// collapse to exactly <c>±ratio</c> or <c>±0</c> and the emitted coordinates reduce to
    /// <c>center ± radius</c> and <c>center ± radius · κ</c> — bit for bit the expressions a
    /// hand-rolled quadrant ellipse writes. That exactness is what
    /// <c>ConvenienceCircleMatchesHandRolledCubicsExactly</c> pins.
    /// </remarks>
    private static void AppendQuadrant(
        PathBuilder path,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float cos0,
        float sin0,
        float cos1,
        float sin1) =>
        AppendSegment(path, centerX, centerY, radiusX, radiusY, cos0, sin0, cos1, sin1, QuarterTurnKappa);

    /// <summary>
    /// Appends the cubic approximating the arc between two unit-circle directions.
    /// </summary>
    /// <remarks>
    /// The tangent to <c>(cos θ, sin θ)</c> in the direction of increasing θ is
    /// <c>(−sin θ, cos θ)</c>, so the first control point leaves the start along its tangent and the
    /// second arrives at the end against its own, both at <paramref name="ratio"/> of the radius.
    /// Anisotropic radii are applied after the unit-circle construction, which is exactly why an
    /// ellipse needs no separate derivation: an affine map of a circle's Bézier is the ellipse's.
    /// </remarks>
    private static void AppendSegment(
        PathBuilder path,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        float cos0,
        float sin0,
        float cos1,
        float sin1,
        float ratio) =>
        path.CubicTo(
            centerX + (radiusX * (cos0 - (ratio * sin0))),
            centerY + (radiusY * (sin0 + (ratio * cos0))),
            centerX + (radiusX * (cos1 + (ratio * sin1))),
            centerY + (radiusY * (sin1 - (ratio * cos1))),
            centerX + (radiusX * cos1),
            centerY + (radiusY * sin1));

    /// <summary>Validates one radius component.</summary>
    private static float RequireRadius(float radius, string parameterName)
    {
        if (!float.IsFinite(radius) || radius < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                radius,
                "A shape radius must be finite and nonnegative.");
        }

        return radius;
    }
}
