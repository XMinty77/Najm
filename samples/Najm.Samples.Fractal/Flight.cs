using System.Numerics;

namespace Najm.Samples.Fractal;

/// <summary>Supplies the shader's state for one moment of the clip.</summary>
internal interface IFlight
{
    /// <summary>Evaluates the uniforms at <paramref name="seconds"/> of simulated time.</summary>
    FractalUniforms At(double seconds);
}

/// <summary>One fixed frame, for looking at a chosen place in the set while tuning.</summary>
internal sealed class FixedFlight(FractalUniforms uniforms) : IFlight
{
    /// <inheritdoc />
    public FractalUniforms At(double seconds) => uniforms;
}

/// <summary>
/// The camera move: one descent into the seahorse valley, a beat at the bottom where the iteration
/// limit is the subject, and a pull back to a composed final frame — turning throughout.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Depth is measured in nepers, not in a normalized parameter.</strong> A zoom is
/// exponential, so the quantity a viewer perceives as speed is <c>d(ln magnification)/dt</c>. Every
/// phase below is written as a rate in nepers per second and integrated; that is the only way to
/// say "hold this speed" and have it mean anything.
/// </para>
/// <para>
/// <strong>The descent has a trapezoidal speed profile, not an eased one.</strong> Smootherstep over
/// the whole descent peaks at 1.875x its own average, so a descent deep enough to be worth watching
/// spends its middle seconds as a blur and its ends barely moving. Accelerating into a constant rate,
/// holding it, and decelerating out gives the same depth at the peak speed of the eased version's
/// <em>average</em> — which is what a fractal zoom that reads as flight rather than as a transition
/// actually does. The acceleration ramps are themselves smootherstep, so there is no kink anywhere.
/// </para>
/// <para>
/// <strong>The pan converges geometrically with the zoom.</strong> A centre interpolated linearly
/// against an exponential zoom slides across the screen faster and faster until the last seconds are
/// unwatchable. Here the remaining centre offset is tied to a power of the remaining zoom, which
/// makes the drift <em>in screen half-heights</em> roughly constant and then gently converge. The
/// lateral arc is scaled by the same factor, so the path curves without ever swinging the frame.
/// </para>
/// <para>
/// <strong>Rotation rides the same profile</strong> rather than having a schedule of its own, which
/// is what makes zoom and turn read as one gesture instead of two effects running at once.
/// </para>
/// <para>
/// <strong>Depth is bounded by single precision, on purpose.</strong> See NOTES.md, "Precision":
/// <see cref="ScaleEnd"/> sits well above the depth at which this shader's <c>float</c> iteration
/// visibly quantizes, measured by rendering stills rather than by arithmetic.
/// </para>
/// </remarks>
internal sealed class Flight : IFlight
{
    /// <summary>The complex half-height of the opening frame. The whole set, with air around it.</summary>
    private const double ScaleStart = 1.30d;

    /// <summary>The complex half-height at the bottom of the descent — about 26 000x.</summary>
    private const double ScaleEnd = 5.0e-5d;

    /// <summary>Where the flight begins, framed so the set sits right of centre.</summary>
    private const double StartX = -0.62d;
    private const double StartY = 0.02d;

    /// <summary>
    /// The destination: the seahorse valley on the neck between the cardioid and the period-2 bulb.
    /// Chosen for the spiral chain that fills the frame at the bottom of the descent.
    /// </summary>
    private const double TargetX = -0.74364386269d;
    private const double TargetY = 0.13182590271d;

    /// <summary>How fast the remaining centre offset decays relative to the remaining zoom.</summary>
    /// <remarks>
    /// One would hold the on-screen drift exactly constant. Slightly above one lets the pan run at
    /// the top, where there is room for it, and settle at the bottom, where there is not.
    /// </remarks>
    private const double PanConvergence = 1.16d;

    /// <summary>The lateral bow of the path, as a fraction of the straight-line distance.</summary>
    private const double PanBow = 0.55d;

    /// <summary>Total rotation of the descent in radians, and where the ascent unwinds it to.</summary>
    private const double RotationSweep = 1.46d;
    private const double RotationSettled = 0.34d;

    /// <summary>How much rotation the beat carries on with after the depth has settled.</summary>
    private const double RotationCarry = 0.16d;

    // The descent's speed profile: accelerate, cruise, decelerate.
    private const double AccelStart = 0.30d;
    private const double CruiseStart = 2.10d;
    private const double CruiseEnd = 6.60d;
    private const double DescentEnd = 7.65d;

    /// <summary>The beat: depth all but still while the iteration limit does the talking.</summary>
    private const double BeatEnd = 10.10d;

    /// <summary>How much deeper the beat creeps, in nepers. Enough that the frame is not frozen.</summary>
    private const double BeatCreep = 0.20d;

    private const double AscentEnd = 12.55d;

    /// <summary>How far the ascent rises, in nepers. About nine times back out.</summary>
    private const double AscentRise = 2.25d;

    private static readonly double TotalNepers = Math.Log(ScaleStart / ScaleEnd);

    /// <summary>The cruise rate in nepers per second, set by making the profile's area come out right.</summary>
    /// <remarks>
    /// A smootherstep ramp integrates to exactly half its box, so the profile's area is
    /// <c>half the accel + all the cruise + half the decel</c> seconds at the cruise rate. Solving
    /// that for the rate is what pins the descent to <see cref="ScaleEnd"/> without anyone having to
    /// tune a constant against it.
    /// </remarks>
    private static readonly double CruiseRate =
        TotalNepers /
        ((0.5d * (CruiseStart - AccelStart)) + (CruiseEnd - CruiseStart) + (0.5d * (DescentEnd - CruiseEnd)));

    /// <inheritdoc />
    public FractalUniforms At(double seconds)
    {
        var t = Math.Clamp(seconds, 0d, Design.ClipSeconds);
        var nepers = Depth(t);
        var d = nepers / TotalNepers;
        var scale = ScaleStart * Math.Exp(-nepers);
        var (centreX, centreY) = Centre(scale);

        return new FractalUniforms
        {
            CentreX = centreX,
            CentreY = centreY,
            Scale = scale,
            Rotation = Rotation(t, d),
            MaxIterations = (float)Iterations(t, d),

            // Fixed, not drifting. A scrolling palette is the cheap way to make a fractal look
            // alive, and it wraps the far field through rust and plum on the way past; the colours
            // here already evolve, because the depth moves both the band period and the floor.
            PaletteShift = 0.015f,
            Bands = (float)(1d / BandPeriod(d)),
            NuFloor = (float)NuFloor(scale),
            RimGain = 0.20f,
            FrontGain = 0.30f,
            Exposure = 1.10f,
        };
    }

    /// <summary>Smootherstep: zero first <em>and</em> second derivative at both ends.</summary>
    private static double Ease(double x)
    {
        x = Math.Clamp(x, 0d, 1d);
        return x * x * x * ((x * ((x * 6d) - 15d)) + 10d);
    }

    private static double Ease(double value, double from, double to) => Ease((value - from) / (to - from));

    private static double Fraction(double value, double from, double to) =>
        Math.Clamp((value - from) / (to - from), 0d, 1d);

    /// <summary>The definite integral of <see cref="Ease(double)"/> from zero to <paramref name="x"/>.</summary>
    /// <remarks>
    /// Closed form, so the depth is an exact function of time rather than an accumulation. It has to
    /// be: an accumulated zoom depends on the frame rate it was accumulated at, and a still exported
    /// at 4.5 s would then not be the frame the video shows at 4.5 s.
    /// </remarks>
    private static double EaseIntegral(double x)
    {
        x = Math.Clamp(x, 0d, 1d);
        var x4 = x * x * x * x;
        return (x4 * x * x) - (3d * x4 * x) + (2.5d * x4);
    }

    /// <summary>The depth at time <paramref name="t"/>, in nepers of magnification.</summary>
    private static double Depth(double t)
    {
        if (t <= AccelStart)
        {
            return 0d;
        }

        var accelArea = (CruiseStart - AccelStart) * CruiseRate;
        if (t <= CruiseStart)
        {
            return accelArea * EaseIntegral(Fraction(t, AccelStart, CruiseStart));
        }

        var afterAccel = 0.5d * accelArea;
        if (t <= CruiseEnd)
        {
            return afterAccel + ((t - CruiseStart) * CruiseRate);
        }

        var afterCruise = afterAccel + ((CruiseEnd - CruiseStart) * CruiseRate);
        if (t <= DescentEnd)
        {
            // Decelerating: the rate is one minus the ease, so the area is the box minus the eased
            // integral.
            var u = Fraction(t, CruiseEnd, DescentEnd);
            var decelArea = (DescentEnd - CruiseEnd) * CruiseRate;
            return afterCruise + (decelArea * (u - EaseIntegral(u)));
        }

        if (t <= BeatEnd)
        {
            return TotalNepers + (BeatCreep * Ease(t, DescentEnd, BeatEnd));
        }

        return TotalNepers + BeatCreep - (AscentRise * Ease(t, BeatEnd, AscentEnd));
    }

    /// <summary>
    /// The frame's rotation. It rides the depth profile through the descent, keeps a little of its
    /// own momentum through the beat, and unwinds on the way out.
    /// </summary>
    /// <remarks>
    /// The small lead-in term is the difference between an establishing shot and a frozen frame:
    /// the depth profile is deliberately still for its first third of a second, and a frame that
    /// does not move at all for that long reads as a stall before the clip has started.
    /// </remarks>
    private static double Rotation(double t, double d)
    {
        if (t <= DescentEnd)
        {
            return RotationSweep * ((0.08d * Ease(t, 0d, 1.7d)) + (0.92d * d));
        }

        if (t <= BeatEnd)
        {
            return RotationSweep + (RotationCarry * Ease(t, DescentEnd, BeatEnd));
        }

        var turned = RotationSweep + RotationCarry;
        return turned + ((RotationSettled - turned) * Ease(t, BeatEnd, AscentEnd));
    }

    /// <summary>The band period, in smooth iterations per ramp cycle.</summary>
    /// <remarks>
    /// It has to widen with depth. Escape-time contours crowd together as the frame shrinks, so a
    /// period that reads as a handful of broad bands at the surface reads as moire at the bottom.
    /// </remarks>
    private static double BandPeriod(double d) => 20d + (78d * Math.Clamp(d, 0d, 1.1d));

    /// <summary>The smooth iteration count the ramp's zero sits at, from the frame's scale.</summary>
    /// <remarks>
    /// <para>
    /// Nothing in a frame escapes in fewer iterations than that depth's own floor, so a ramp
    /// anchored at zero spends its first cycle on counts that never occur and leaves the visible
    /// field wherever the arithmetic happens to put it — which at the bottom of this descent was a
    /// wash of plum across a third of the frame.
    /// </para>
    /// <para>
    /// <strong>Measured, not guessed.</strong> A CPU evaluation of the smooth iteration count over
    /// this flight's own frames, sampled at eight depths, puts the first-percentile count at
    /// <c>4.6 + 4.2 ln(magnification)</c> to within a fraction of an iteration at every one of them.
    /// The linearity is not a coincidence: escape time from a point a distance d outside the set
    /// grows like log(1/d), and the frame's own scale is what sets that distance.
    /// </para>
    /// </remarks>
    private static double NuFloor(double scale) =>
        4.6d + (4.2d * Math.Max(Math.Log(ScaleStart / scale), 0d));

    /// <summary>The centre, placed by how much zoom is left rather than by how much time is.</summary>
    private static (double X, double Y) Centre(double scale)
    {
        var remaining = Math.Pow(Math.Clamp(scale / ScaleStart, 0d, 1d), PanConvergence);
        var toStart = new Vector2((float)(StartX - TargetX), (float)(StartY - TargetY));
        var perpendicular = new Vector2(-toStart.Y, toStart.X);

        // sin(pi * travelled) bows the path out and brings it back, and `remaining` keeps the bow
        // shrinking with the frame so it is a curve rather than a swerve.
        var bow = PanBow * remaining * Math.Sin(Math.PI * (1d - remaining));
        return (
            TargetX + (toStart.X * remaining) + (perpendicular.X * bow),
            TargetY + (toStart.Y * remaining) + (perpendicular.Y * bow));
    }

    /// <summary>
    /// The iteration limit: what the depth needs, times a swing that is the subject of one beat.
    /// </summary>
    /// <remarks>
    /// The requirement grows with the depth for the same reason the colour floor does (see
    /// <see cref="NuFloor"/>) — escape time grows with the log of the magnification. The beat then
    /// takes the limit far below what the depth needs, so filaments dissolve back into the body of
    /// the set, and overshoots above it on the way back, which is when the finest structure appears.
    /// </remarks>
    private static double Iterations(double t, double d)
    {
        var required = 120d + (1750d * Math.Pow(Math.Max(d, 0d), 1.15d));
        return required * Swing(t);
    }

    private static double Swing(double t)
    {
        const double Trough = DescentEnd + 1.05d;
        const double Crest = DescentEnd + 2.15d;

        if (t <= DescentEnd)
        {
            return 1d;
        }

        if (t <= Trough)
        {
            return 1d + ((0.16d - 1d) * Ease(t, DescentEnd, Trough));
        }

        if (t <= Crest)
        {
            return 0.16d + ((1.38d - 0.16d) * Ease(t, Trough, Crest));
        }

        return 1.38d + ((1d - 1.38d) * Ease(t, Crest, AscentEnd));
    }
}
