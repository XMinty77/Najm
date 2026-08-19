using System.Numerics;
using Najm.Utils;

namespace Najm.Samples.Orrery;

/// <summary>
/// Every number that decides how the piece looks, in one place. Nothing here is astronomy: the
/// orbital periods are chosen so the whole image repeats exactly once per
/// <see cref="LoopSeconds"/>, and the radii are chosen so no two bodies sweep the frame at wildly
/// different apparent speeds.
/// </summary>
internal static class Design
{
    /// <summary>The loop length. Every periodic quantity in the scene divides it exactly.</summary>
    public const double LoopSeconds = 15d;

    /// <summary>How far the ecliptic is foreshortened. 1 is a plan view, 0 is edge-on.</summary>
    public const float Tilt = 0.38f;

    /// <summary>Where the sun sits in the 1920x1080 frame, off-center and low.</summary>
    public static readonly Vector2 SunInFrame = new(764f, 672f);

    /// <summary>
    /// A few degrees of roll on the whole ecliptic. Axis-aligned ellipses read as a diagram; a
    /// tilted stack of them reads as a photograph of something.
    /// </summary>
    public static readonly Angle Roll = Angle.Deg(-8d);

    /// <summary>Arc length, in virtual units, of the light each body drags behind it.</summary>
    public const float TrailLength = 150f;

    public static Color Space { get; } = Color.OkLch(0.150f, 0.030f, 258d).ClampToSrgbGamut();

    public static Color Haze { get; } = Color.OkLch(0.640f, 0.090f, 62d).ClampToSrgbGamut();

    public static Color SunCore { get; } = Color.OkLch(0.985f, 0.040f, 92d).ClampToSrgbGamut();

    public static Color SunGlow { get; } = Color.OkLch(0.860f, 0.130f, 74d).ClampToSrgbGamut();

    public static Color Ring { get; } = Color.OkLch(0.800f, 0.030f, 246d).ClampToSrgbGamut();

    public static Color Star { get; } = Color.OkLch(0.930f, 0.020f, 240d).ClampToSrgbGamut();

    public static Color Dust { get; } = Color.OkLch(0.800f, 0.022f, 78d).ClampToSrgbGamut();

    /// <summary>
    /// The bodies, inner to outer. Warm hues near the sun, cool ones far from it, all at low
    /// chroma so six bodies read as one family rather than as a chart legend.
    /// </summary>
    public static BodySpec[] Bodies { get; } =
    [
        new BodySpec
        {
            OrbitRadius = 118f, Revolutions = 7, Phase = 0.533f, Radius = 6.5f,
            Color = Color.OkLch(0.900f, 0.075f, 78d).ClampToSrgbGamut(),
        },
        new BodySpec
        {
            OrbitRadius = 170f, Revolutions = 5, Phase = 0.699f, Radius = 9f,
            Color = Color.OkLch(0.865f, 0.085f, 42d).ClampToSrgbGamut(),
        },
        new BodySpec
        {
            OrbitRadius = 236f, Revolutions = 4, Phase = 0.056f, Radius = 8f,
            Color = Color.OkLch(0.885f, 0.040f, 232d).ClampToSrgbGamut(),
        },
        new BodySpec
        {
            OrbitRadius = 328f, Revolutions = 3, Phase = 0.394f, Radius = 12f,
            Color = Color.OkLch(0.845f, 0.070f, 26d).ClampToSrgbGamut(),
            MoonRevolutions = 8, MoonDistance = 36f, MoonRadius = 3.2f,
        },
        new BodySpec
        {
            OrbitRadius = 470f, Revolutions = 2, Phase = 0.402f, Radius = 25f,
            Color = Color.OkLch(0.875f, 0.062f, 214d).ClampToSrgbGamut(),
            HasRing = true,
        },
        new BodySpec
        {
            OrbitRadius = 700f, Revolutions = 1, Phase = 0.159f, Radius = 16f,
            Color = Color.OkLch(0.820f, 0.055f, 252d).ClampToSrgbGamut(),
        },
    ];
}

/// <summary>One body's fixed description. Nothing here changes after load.</summary>
internal sealed class BodySpec
{
    /// <summary>Semi-major axis, in virtual units.</summary>
    public required float OrbitRadius { get; init; }

    /// <summary>
    /// Whole revolutions completed in one loop. It must be an integer, and that single constraint
    /// is what makes the clip seamless.
    /// </summary>
    public required int Revolutions { get; init; }

    /// <summary>Starting position around the orbit, in turns.</summary>
    public required float Phase { get; init; }

    /// <summary>Drawn body radius, in virtual units.</summary>
    public required float Radius { get; init; }

    public required Color Color { get; init; }

    public bool HasRing { get; init; }

    public int MoonRevolutions { get; init; }

    public float MoonDistance { get; init; }

    public float MoonRadius { get; init; }

    public bool HasMoon => MoonRevolutions > 0;

    public float SemiMinorAxis => OrbitRadius * Design.Tilt;
}
