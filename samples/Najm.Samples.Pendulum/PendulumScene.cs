using Najm.Core;

namespace Najm.Samples.Pendulum;

/// <summary>
/// Several double pendulums released from almost-identical initial conditions. Left: the
/// pendulums, drawn and animated. Right: a phase-space view of each pendulum's lower bob
/// (θ2, ω2), one fading trail and one point per pendulum.
/// </summary>
/// <remarks>
/// The scene is entirely a function of accumulated <see cref="Update"/> state — a fixed-step RK4
/// integration — so there is nothing time-parametric to close into a loop and no coroutine driving
/// anything; per SAMPLES.md's own note, the scheduler is not needed here.
/// </remarks>
internal sealed class PendulumScene : Scene
{
    private readonly PendulumInstance[] pendulums;

    public PendulumScene()
    {
        pendulums = new PendulumInstance[Design.PendulumCount];
        for (var i = 0; i < pendulums.Length; i++)
        {
            pendulums[i] = new PendulumInstance(
                Design.Physics,
                theta1: Design.Theta0 + Design.ThetaOffset(i),
                theta2: Design.Theta0,
                accent: Design.Accent(i));
        }
    }

    protected override void OnLoad()
    {
        var screen = Layers.Add(new ScreenLayer { ClearColor = Design.Background });
        var root = screen.Root;

        root.Add(new DividerNode());

        var pivotGroup = root.Add(new Node2D { Position = Design.PivotPx });
        foreach (var pendulum in pendulums)
        {
            pivotGroup.Add(new PendulumArmsNode(pendulum));
        }
        pivotGroup.Add(new PivotNode());

        root.Add(new PhasePanelFrameNode());

        // HARD requirement: every trail beneath every point, genuinely by ZIndex — two sibling
        // groups, not per-pendulum interleaving and not insertion-order luck.
        var phaseGroup = root.Add(new Node2D());
        var trailsGroup = phaseGroup.Add(new Node2D { ZIndex = 0 });
        var pointsGroup = phaseGroup.Add(new Node2D { ZIndex = 1 });
        foreach (var pendulum in pendulums)
        {
            trailsGroup.Add(new PhaseTrailNode(pendulum));
            pointsGroup.Add(new PhasePointNode(pendulum));
        }
    }

    protected override void Update(in TickContext tick)
    {
        foreach (var pendulum in pendulums)
        {
            pendulum.Advance(tick.Time.Dt);
        }
    }
}
