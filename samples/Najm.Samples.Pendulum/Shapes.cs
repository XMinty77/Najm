using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Pendulum;

/// <summary>
/// Soft-glow geometry the engine does not ship. Circles themselves used to need a hand-rolled cubic
/// approximation here (the same workaround the Orrery sample needed) until Tier-2's
/// <see cref="IDrawContext2D.DrawCircle"/> landed mid-session — see NOTES.md. A radial-falloff glow
/// brush has no engine convenience of its own, so it stays.
/// </summary>
internal static class Shapes
{
    /// <summary>A soft radial falloff used as an additive glow behind a solid disc at the local origin.</summary>
    public static Brush Glow(Color color, float radius, float peakAlpha) =>
        Glow(color, Vector2.Zero, radius, peakAlpha);

    /// <inheritdoc cref="Glow(Color, float, float)" />
    /// <param name="center">
    /// The glow's center in the same local coordinates as the geometry it fills — a gradient brush
    /// is positioned independently of the path it paints, so this must match the circle's center or
    /// the two drift apart.
    /// </param>
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
}
