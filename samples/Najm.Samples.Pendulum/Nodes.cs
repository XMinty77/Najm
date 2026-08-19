using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Pendulum;

/// <summary>The shared pivot every pendulum swings from.</summary>
internal sealed class PivotNode : Drawable
{
    public override Rect VisualBounds =>
        new(-Design.PivotRadius - 2f, -Design.PivotRadius - 2f, (Design.PivotRadius + 2f) * 2f, (Design.PivotRadius + 2f) * 2f);

    public override void Render(IDrawContext2D context)
    {
        context.DrawCircle(Vector2.Zero, Design.PivotRadius, Paint.Fill(Design.PivotColor));
        context.DrawCircle(Vector2.Zero, Design.PivotRadius, Paint.Stroke(Design.Background, 1.5f));
    }
}

/// <summary>
/// One pendulum's two arms and two bobs, drawn relative to the shared pivot at this node's parent
/// origin. A single node per pendulum, since paint order among pendulums carries no requirement —
/// only the phase-space trail/point split does (see <see cref="PhaseTrailNode"/>).
/// </summary>
internal sealed class PendulumArmsNode : Drawable
{
    private readonly PendulumInstance pendulum;
    private readonly Paint armPaint;

    public PendulumArmsNode(PendulumInstance pendulum)
    {
        this.pendulum = pendulum;
        armPaint = Paint.Stroke(Design.ArmColor.WithAlpha(0.55f), Design.ArmWidth, cap: LineCap.Round);
    }

    public override Rect VisualBounds => new(-460f, -460f, 920f, 920f);

    public override void Render(IDrawContext2D context)
    {
        var bob1 = pendulum.Bob1Local;
        var bob2 = pendulum.Bob2Local;

        context.DrawLine(Vector2.Zero, bob1, armPaint);
        context.DrawLine(bob1, bob2, armPaint);

        context.DrawCircle(bob1, Design.Bob1Radius, Paint.Fill(pendulum.Accent.WithAlpha(0.85f)));

        context.DrawCircle(
            bob2,
            Design.Bob2Radius * 3.2f,
            Paint.Fill(Shapes.Glow(pendulum.Accent, bob2, Design.Bob2Radius * 3.2f, 0.35f), blendMode: BlendMode.Plus));
        context.DrawCircle(bob2, Design.Bob2Radius, Paint.Fill(pendulum.Accent));
    }
}

/// <summary>
/// The static decoration for the phase-space panel: a border and the θ = 0 / ω = 0 center lines.
/// </summary>
internal sealed class PhasePanelFrameNode : Drawable
{
    public override Rect VisualBounds => new(Design.PhaseX0 - 4f, Design.PhaseY0 - 4f, Design.PhaseWidth + 8f, Design.PhaseHeight + 8f);

    public override void Render(IDrawContext2D context)
    {
        var x0 = Design.PhaseX0;
        var y0 = Design.PhaseY0;
        var x1 = Design.PhaseX0 + Design.PhaseWidth;
        var y1 = Design.PhaseY0 + Design.PhaseHeight;
        var midX = x0 + (Design.PhaseWidth * 0.5f);
        var midY = y0 + (Design.PhaseHeight * 0.5f);
        var gridPaint = Paint.Stroke(Design.GridColor.WithAlpha(0.5f), 1f);

        context.DrawLine(new Vector2(midX, y0), new Vector2(midX, y1), gridPaint);
        context.DrawLine(new Vector2(x0, midY), new Vector2(x1, midY), gridPaint);
        context.DrawRect(new Rect(x0, y0, Design.PhaseWidth, Design.PhaseHeight), Paint.Stroke(Design.FrameColor, 1.5f));
    }
}

/// <summary>A faint vertical seam separating the pendulum panel from the phase-space panel.</summary>
internal sealed class DividerNode : Drawable
{
    public override Rect VisualBounds => new(956f, 40f, 8f, 1000f);

    public override void Render(IDrawContext2D context) =>
        context.DrawLine(new Vector2(960f, 60f), new Vector2(960f, 1020f), Paint.Stroke(Design.DividerColor, 1f));
}

/// <summary>
/// One pendulum's phase-space trail: its recent (θ2, ω2) history as a Catmull-Rom spline that fades
/// and tapers toward its tail, drawn with one <see cref="IDrawContext2D.DrawGradientSpline"/> call
/// per contiguous run.
/// </summary>
/// <remarks>
/// This was N hand-rolled <see cref="IDrawContext2D.DrawPath"/> calls, one per cubic, each with its
/// own <see cref="Paint"/> — the workaround NOTES.md reported and the Orrery sample had written
/// independently. The engine owns the loop now: the ramp is stated per sample, where age is known,
/// and the convenience resolves it per segment and abuts the joins so the translucent strokes stop
/// beading where they meet.
/// </remarks>
internal sealed class PhaseTrailNode : Drawable
{
    private readonly PendulumInstance pendulum;
    private readonly Paint trailPaint;

    /// <summary>Per-sample ramp, refilled each frame and sliced per run; never reallocated.</summary>
    private readonly Color[] colors = new Color[Design.TrailCapacity];
    private readonly float[] widths = new float[Design.TrailCapacity];

    public PhaseTrailNode(PendulumInstance pendulum)
    {
        this.pendulum = pendulum;

        // The template supplies everything that does not vary along the trail. Its color is
        // unused — the ramp replaces it — and the round cap rounds each run's two outer ends, the
        // newest of which sits under the phase point's own disc anyway.
        trailPaint = Paint.Stroke(pendulum.Accent, 1f, cap: LineCap.Round);
    }

    public override Rect VisualBounds => new(Design.PhaseX0, Design.PhaseY0, Design.PhaseWidth, Design.PhaseHeight);

    public override void Render(IDrawContext2D context)
    {
        var points = pendulum.Trail.Snapshot();
        var total = points.Length;
        if (total < 2)
        {
            return;
        }

        // A centripetal Catmull-Rom segment cannot self-intersect or cusp, but it can still bulge
        // slightly outside the bounding box of its four control points near a sharp turn — clip to
        // the panel so that never reads as content leaking past its own frame.
        context.PushClip(VisualBounds);

        // Age runs off each sample's true position in the whole trail — 0 at the tail, 1 at the
        // newest sample — so the fade stays continuous across the wrap breaks below.
        var last = total - 1;
        for (var i = 0; i < total; i++)
        {
            var age = i / (float)last;
            colors[i] = pendulum.Accent.WithAlpha(0.65f * age * age);
            widths[i] = 0.9f + (2.6f * age);
        }

        // θ2 wraps to [-π, π] (Design.PhasePixel), so a sample-to-sample jump near the panel's
        // full width is not real motion — it is the same physical angle re-entering from the other
        // side. Splining across that jump would draw a spurious chord clear across the panel, so
        // each contiguous run between wraps is its own spline, sharing the one ramp above.
        var runStart = 0;
        for (var i = 1; i <= total; i++)
        {
            var atBreak = i == total || MathF.Abs(points[i].X - points[i - 1].X) > Design.PhaseWidth * 0.5f;
            if (!atBreak)
            {
                continue;
            }

            var runLength = i - runStart;
            if (runLength >= 2)
            {
                context.DrawGradientSpline(
                    points.Slice(runStart, runLength),
                    colors.AsSpan(runStart, runLength),
                    widths.AsSpan(runStart, runLength),
                    trailPaint);
            }

            runStart = i;
        }

        context.PopClip();
    }
}

/// <summary>One pendulum's current phase-space point: a soft glow behind a solid disc.</summary>
internal sealed class PhasePointNode : Drawable
{
    private readonly PendulumInstance pendulum;

    public PhasePointNode(PendulumInstance pendulum) => this.pendulum = pendulum;

    public override Rect VisualBounds => new(Design.PhaseX0, Design.PhaseY0, Design.PhaseWidth, Design.PhaseHeight);

    public override void Render(IDrawContext2D context)
    {
        var at = pendulum.PhasePointPx;

        context.DrawCircle(
            at,
            Design.PhasePointRadius * 3f,
            Paint.Fill(Shapes.Glow(pendulum.Accent, at, Design.PhasePointRadius * 3f, 0.4f), blendMode: BlendMode.Plus));
        context.DrawCircle(at, Design.PhasePointRadius, Paint.Fill(pendulum.Accent));
        context.DrawCircle(at, Design.PhasePointRadius, Paint.Stroke(Design.Background, 1.25f));
    }
}
