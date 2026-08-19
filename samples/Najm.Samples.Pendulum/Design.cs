using System.Numerics;
using Najm.Utils;

namespace Najm.Samples.Pendulum;

/// <summary>All tunable numbers for the scene in one place, in the Orrery sample's spirit.</summary>
internal static class Design
{
    public const int Fps = 60;
    public const double ClipSeconds = 18d;

    // --- Physics ---
    public static readonly DoublePendulumParams Physics = new(M1: 1d, M2: 1d, L1: 1d, L2: 1d, G: 9.81d);
    public const int Substeps = 20; // RK4 substeps per 1/60s tick; verified energy-conserving (see NOTES.md).
    public const double Theta0 = 2.2d; // radians from downward vertical, shared start for both rods.
    public const int PendulumCount = 5;

    /// <summary>Per-pendulum tiny opening offset on θ1, radians. "Almost identical" initial conditions.</summary>
    public static double ThetaOffset(int index) => index * 2.5e-4d;

    public const int TrailCapacity = 110; // ~1.83s of history at 60 Hz.
    public const double OmegaMax = 16d; // rad/s; observed peak ~13.4, margin for divergence.

    // --- Left panel: pendulums, in virtual pixels ---
    public static readonly Vector2 PivotPx = new(480f, 460f);
    public const float ArmScale = 200f; // pixels per physics length unit.
    public const float Bob1Radius = 7f;
    public const float Bob2Radius = 9.5f;
    public const float ArmWidth = 2.25f;
    public const float PivotRadius = 6f;

    // --- Right panel: phase space, in virtual pixels ---
    public const float PhaseX0 = 1040f;
    public const float PhaseY0 = 110f;
    public const float PhaseWidth = 840f;
    public const float PhaseHeight = 840f;
    public const float PhasePointRadius = 8f;

    /// <summary>Maps (θ2 wrapped to [-π,π], ω2) to absolute virtual pixel coordinates.</summary>
    public static Vector2 PhasePixel(double theta2, double omega2)
    {
        var wrapped = WrapAngle(theta2);
        var clampedOmega = Math.Clamp(omega2, -OmegaMax, OmegaMax);

        var tx = (wrapped + Math.PI) / (2d * Math.PI);
        var ty = 0.5d - (clampedOmega / (2d * OmegaMax));

        return new Vector2(
            PhaseX0 + (float)(tx * PhaseWidth),
            PhaseY0 + (float)(ty * PhaseHeight));
    }

    public static double WrapAngle(double angle)
    {
        var wrapped = angle % (2d * Math.PI);
        if (wrapped > Math.PI)
        {
            wrapped -= 2d * Math.PI;
        }
        else if (wrapped < -Math.PI)
        {
            wrapped += 2d * Math.PI;
        }

        return wrapped;
    }

    // --- Palette: a restrained cool-to-warm OKLCH arc, one hue per pendulum ---
    private static readonly double[] Hues = [205d, 258d, 312d, 350d, 28d];

    public static Color Accent(int index) =>
        Color.OkLch(0.80f, 0.145f, Hues[index % Hues.Length]).ClampToSrgbGamut();

    public static readonly Color Background = Color.OkLch(0.155f, 0.022f, 250f).ClampToSrgbGamut();
    public static readonly Color ArmColor = Color.OkLch(0.72f, 0.01f, 250f).ClampToSrgbGamut();
    public static readonly Color PivotColor = Color.OkLch(0.82f, 0.01f, 250f).ClampToSrgbGamut();
    public static readonly Color FrameColor = Color.OkLch(0.55f, 0.02f, 250f).ClampToSrgbGamut();
    public static readonly Color GridColor = Color.OkLch(0.40f, 0.02f, 250f).ClampToSrgbGamut();
    public static readonly Color DividerColor = Color.OkLch(0.35f, 0.02f, 250f).ClampToSrgbGamut();
}
