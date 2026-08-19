using System.Numerics;

namespace Najm.Samples.Orrery;

/// <summary>
/// One body's state at the current instant. It is a pure function of the loop phase — no
/// integration, no accumulated state — which is what lets any frame be rendered on its own and
/// what makes the last frame meet the first exactly.
/// </summary>
internal sealed class Body
{
    public Body(BodySpec spec) => Spec = spec;

    public BodySpec Spec { get; }

    /// <summary>Position on the foreshortened ellipse, in the system's own Y-up space.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Angle around the orbit, in radians.</summary>
    public float Angle { get; private set; }

    /// <summary>+1 when the body is nearest the viewer, -1 when it is behind the sun.</summary>
    public float Depth { get; private set; }

    /// <summary>Moon offset from the body, in the system's own Y-up space.</summary>
    public Vector2 MoonOffset { get; private set; }

    /// <summary>+1 when the moon is in front of its body, -1 when behind it.</summary>
    public float MoonDepth { get; private set; }

    /// <summary>Half the visual foreshortening of a full turn, used to size trails.</summary>
    public float TrailTurns => Design.TrailLength / (MathF.Tau * Spec.OrbitRadius);

    public void Advance(double loopPhase)
    {
        Angle = (float)(Math.Tau * (Spec.Phase + (Spec.Revolutions * loopPhase)));
        var (sin, cos) = MathF.SinCos(Angle);
        Position = new Vector2(Spec.OrbitRadius * cos, Spec.SemiMinorAxis * sin);

        // World space is Y-up and the camera looks from below the ecliptic, so the near half of
        // every orbit is the half with negative Y.
        Depth = -sin;

        if (!Spec.HasMoon)
        {
            return;
        }

        var moonAngle = Math.Tau * ((Spec.Phase * 3d) + (Spec.MoonRevolutions * loopPhase));
        var (moonSin, moonCos) = Math.SinCos(moonAngle);
        MoonOffset = new Vector2(
            (float)(Spec.MoonDistance * moonCos),
            (float)(Spec.MoonDistance * Design.Tilt * moonSin));
        MoonDepth = (float)-moonSin;
    }
}
