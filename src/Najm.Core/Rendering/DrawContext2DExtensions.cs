using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Scalar-radius spellings of the Tier-2 conveniences.</summary>
/// <remarks>
/// These are extension methods rather than interface members on purpose. They add no behavior — each
/// one forwards to the virtual convenience with an isotropic radius — so making them overridable
/// would invite a backend to give the circular case different geometry from the elliptical one,
/// which is the exact class of divergence <see cref="DrawContext2DBase"/> exists to prevent. As
/// extensions they are visible on <see cref="IDrawContext2D"/> and on every concrete context alike,
/// and they allocate nothing.
/// </remarks>
public static class DrawContext2DExtensions
{
    /// <summary>Fills or strokes an axis-aligned ellipse given its two semi-axes separately.</summary>
    /// <param name="context">The draw context.</param>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radiusX">The finite nonnegative local-unit horizontal semi-axis.</param>
    /// <param name="radiusY">The finite nonnegative local-unit vertical semi-axis.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static void DrawEllipse(
        this IDrawContext2D context,
        in Vector2 center,
        float radiusX,
        float radiusY,
        in Paint paint)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.DrawEllipse(center, new Vector2(radiusX, radiusY), paint);
    }

    /// <summary>Fills or strokes a rectangle whose corners are circular quarter turns.</summary>
    /// <param name="context">The draw context.</param>
    /// <param name="bounds">The rectangle, in local units.</param>
    /// <param name="cornerRadius">
    /// The finite nonnegative local-unit corner radius, clamped per axis to half the corresponding
    /// side. Clamping is per axis, so an over-large radius on a non-square rectangle produces a
    /// stadium rather than a circle.
    /// </param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static void DrawRoundRect(
        this IDrawContext2D context,
        in Rect bounds,
        float cornerRadius,
        in Paint paint)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.DrawRoundRect(bounds, new Vector2(cornerRadius, cornerRadius), paint);
    }

    /// <summary>
    /// Strokes the Catmull-Rom spline through a run of control points, with color and width varying
    /// along its length.
    /// </summary>
    /// <param name="context">The draw context.</param>
    /// <param name="points">The control points, in order; the span is not retained.</param>
    /// <param name="vertexColors">One color per control point, or an empty span to use the template's.</param>
    /// <param name="vertexWidths">One width per control point, or an empty span to use the template's.</param>
    /// <param name="template">The stroke the spline is painted with.</param>
    /// <param name="closed">
    /// Whether the spline wraps from the last control point back to the first, with no repeat of the
    /// first point at the end.
    /// </param>
    /// <param name="alpha">
    /// The parameterization exponent in [0, 1], centripetal by default. See
    /// <see cref="CatmullRom"/> for why that default is not uniform — sampled trajectories are
    /// exactly the unevenly spaced data uniform parameterization cusps on.
    /// </param>
    /// <remarks>
    /// The spelling for the common case, where the author has control points rather than a
    /// <see cref="CatmullRomSegments"/> already in hand. It adds nothing:
    /// <see cref="CatmullRom.Open(ReadOnlySpan{Vector2}, float)"/> or
    /// <see cref="CatmullRom.Closed(ReadOnlySpan{Vector2}, float)"/>, then
    /// <see cref="IDrawContext2D.DrawGradientSpline"/>, which owns the whole contract.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static void DrawGradientSpline(
        this IDrawContext2D context,
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template,
        bool closed = false,
        float alpha = CatmullRom.CentripetalAlpha)
    {
        ArgumentNullException.ThrowIfNull(context);

        var spline = closed
            ? CatmullRom.Closed(points, alpha)
            : CatmullRom.Open(points, alpha);
        context.DrawGradientSpline(spline, vertexColors, vertexWidths, template);
    }

    /// <summary>Fills or strokes a circular arc.</summary>
    /// <param name="context">The draw context.</param>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radius">The finite nonnegative local-unit radius.</param>
    /// <param name="startAngle">Where the arc begins, measured from <c>center + (radius, 0)</c>.</param>
    /// <param name="sweepAngle">
    /// How far the arc turns; positive turns from +x toward +y, which is clockwise on screen.
    /// </param>
    /// <param name="mode">How the arc's two ends are joined into a contour.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static void DrawArc(
        this IDrawContext2D context,
        in Vector2 center,
        float radius,
        Angle startAngle,
        Angle sweepAngle,
        ArcMode mode,
        in Paint paint)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.DrawArc(center, new Vector2(radius, radius), startAngle, sweepAngle, mode, paint);
    }
}
