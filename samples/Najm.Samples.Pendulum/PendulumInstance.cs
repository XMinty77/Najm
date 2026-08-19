using System.Numerics;
using Najm.Utils;

namespace Najm.Samples.Pendulum;

/// <summary>
/// One pendulum's physics plus everything derived from it that the nodes need to draw: bob
/// positions in left-panel local pixels (relative to the shared pivot) and the phase-space trail.
/// </summary>
/// <remarks>
/// Positions are computed once per tick in <see cref="Advance"/> and simply read back by
/// <c>Render</c>, which keeps rendering idempotent — the nodes never touch the physics state
/// themselves.
/// </remarks>
internal sealed class PendulumInstance
{
    private readonly DoublePendulum physics;

    public PendulumInstance(DoublePendulumParams parameters, double theta1, double theta2, Color accent)
    {
        physics = new DoublePendulum(parameters, theta1, theta2);
        Accent = accent;
        Trail = new TrailBuffer(Design.TrailCapacity);
        Sync();
    }

    public Color Accent { get; }

    public TrailBuffer Trail { get; }

    public Vector2 Bob1Local { get; private set; }

    public Vector2 Bob2Local { get; private set; }

    public Vector2 PhasePointPx { get; private set; }

    /// <summary>Steps the physics forward and refreshes every derived, render-facing quantity.</summary>
    public void Advance(double dt)
    {
        physics.Step(dt, Design.Substeps);
        Sync();
        Trail.Push(PhasePointPx);
    }

    /// <summary>Recomputes derived positions from the current physics state without stepping.</summary>
    /// <remarks>Called from the constructor so the loaded (t = 0) frame is already correct.</remarks>
    private void Sync()
    {
        var (x1, y1, x2, y2) = physics.BobPositions();
        Bob1Local = new Vector2((float)x1, (float)y1) * Design.ArmScale;
        Bob2Local = new Vector2((float)x2, (float)y2) * Design.ArmScale;
        PhasePointPx = Design.PhasePixel(physics.Theta2, physics.Omega2);
    }
}
