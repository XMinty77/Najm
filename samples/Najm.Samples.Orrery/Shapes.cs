using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Orrery;

/// <summary>
/// Geometry the engine does not ship: circles, ellipses, elliptical arcs, and soft radial
/// falloffs. <see cref="PathBuilder"/> offers only move/line/quad/cubic/close, so every rounded
/// shape in this scene is a hand-rolled cubic approximation.
/// </summary>
internal static class Shapes
{
    /// <summary>The classic circular-arc cubic control-point ratio for a quarter turn.</summary>
    private const float Kappa = 0.5522847498307936f;

    public static PathBuilder AddCircle(this PathBuilder path, float cx, float cy, float r) =>
        path.AddEllipse(cx, cy, r, r);

    public static PathBuilder AddEllipse(this PathBuilder path, float cx, float cy, float rx, float ry)
    {
        var ox = rx * Kappa;
        var oy = ry * Kappa;
        return path
            .MoveTo(cx + rx, cy)
            .CubicTo(cx + rx, cy + oy, cx + ox, cy + ry, cx, cy + ry)
            .CubicTo(cx - ox, cy + ry, cx - rx, cy + oy, cx - rx, cy)
            .CubicTo(cx - rx, cy - oy, cx - ox, cy - ry, cx, cy - ry)
            .CubicTo(cx + ox, cy - ry, cx + rx, cy - oy, cx + rx, cy)
            .Close();
    }

    /// <summary>Appends the half of an axis-aligned ellipse on one side of the x axis.</summary>
    /// <param name="upper">True for the y &gt; 0 half, false for the y &lt; 0 half.</param>
    public static PathBuilder AddEllipseHalf(
        this PathBuilder path,
        float cx,
        float cy,
        float rx,
        float ry,
        bool upper)
    {
        var ox = rx * Kappa;
        var oy = ry * Kappa * (upper ? 1f : -1f);
        var sy = ry * (upper ? 1f : -1f);
        return path
            .MoveTo(cx + rx, cy)
            .CubicTo(cx + rx, cy + oy, cx + ox, cy + sy, cx, cy + sy)
            .CubicTo(cx - ox, cy + sy, cx - rx, cy + oy, cx - rx, cy);
    }

    /// <summary>
    /// Builds the stop ramp of a soft glow: full alpha at the center falling to zero at the rim,
    /// on a curve chosen to look like light rather than like a gradient.
    /// </summary>
    /// <remarks>
    /// The ramp ends at the same RGB with zero alpha rather than at
    /// <see cref="Color.Transparent"/>, because gradient stops interpolate unpremultiplied and a
    /// fade to transparent black would drag a grey bruise through the halo.
    /// </remarks>
    public static Brush Glow(Color color, float radius, float peakAlpha) =>
        Glow(color, Vector2.Zero, radius, peakAlpha);

    /// <inheritdoc cref="Glow(Color, float, float)" />
    public static Brush Glow(Color color, Vector2 center, float radius, float peakAlpha)
    {
        const int Count = 8;
        Span<GradientStop> stops = stackalloc GradientStop[Count];
        for (var i = 0; i < Count; i++)
        {
            var t = i / (float)(Count - 1);
            var falloff = (1f - (t * t)) * (1f - (t * t));
            stops[i] = new GradientStop(t, color.WithAlpha(peakAlpha * falloff));
        }

        return Brush.Radial(center, radius, stops);
    }

    /// <summary>The inverse of <see cref="Glow(Color, float, float)"/>: clear at the center,
    /// opaque at the rim. This is the vignette.</summary>
    public static Brush Falloff(Color color, Vector2 center, float radius, float rimAlpha)
    {
        const int Count = 7;
        Span<GradientStop> stops = stackalloc GradientStop[Count];
        for (var i = 0; i < Count; i++)
        {
            var t = i / (float)(Count - 1);
            var ramp = t * t * t;
            stops[i] = new GradientStop(t, color.WithAlpha(rimAlpha * ramp));
        }

        return Brush.Radial(center, radius, stops);
    }

    /// <summary>Shifts a color's OKLCH lightness and chroma, staying in the same hue family.</summary>
    public static Color Shade(this Color color, float lightnessScale, float chromaScale)
    {
        var (lightness, chroma, hue) = color.ToOkLch();
        return Color.OkLch(lightness * lightnessScale, chroma * chromaScale, hue, color.A)
            .ClampToSrgbGamut();
    }
}
