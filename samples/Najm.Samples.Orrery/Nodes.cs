using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Orrery;

/// <summary>
/// The star at the middle: four additive falloffs stacked into a profile with a hard core and a
/// long tail, plus a solid disc. Najm has no blur and no glow effect yet, so a glow is a stack of
/// radial gradients drawn in <see cref="BlendMode.Plus"/>.
/// </summary>
internal sealed class SunNode : Drawable
{
    private readonly PathBuilder path = new(initialCapacity: 6);
    private float breath = 1f;

    public override Rect VisualBounds => new(-460f, -460f, 920f, 920f);

    /// <summary>A slow swell over exactly one loop, so it meets itself at the seam.</summary>
    public void SetBreath(double loopPhase) =>
        breath = 1f + (0.045f * (float)(1d - Math.Cos(Math.Tau * loopPhase)) * 0.5f);

    public override void Render(IDrawContext2D context)
    {
        Halo(context, 470f * breath, 0.120f);
        Halo(context, 180f * breath, 0.210f);
        Halo(context, 62f * breath, 0.440f);
        Halo(context, 33f, 0.720f);

        Span<GradientStop> core =
        [
            new GradientStop(0f, Design.SunCore),
            new GradientStop(0.62f, Design.SunCore.Shade(0.99f, 2.2f)),
            new GradientStop(1f, Design.SunGlow.Shade(1.02f, 1.1f)),
        ];

        path.Reset();
        path.AddCircle(0f, 0f, 23f);
        context.DrawPath(path, Paint.Fill(Brush.Radial(new Vector2(-4f, 4f), 27f, core)));
    }

    private void Halo(IDrawContext2D context, float radius, float peak)
    {
        path.Reset();
        path.AddCircle(0f, 0f, radius);
        context.DrawPath(
            path,
            Paint.Fill(Shapes.Glow(Design.SunGlow, radius, peak), blendMode: BlendMode.Plus));
    }
}

/// <summary>
/// One orbit: the faint complete ellipse, and the bright arc of light the body drags behind it. The
/// trail is one ramped run per pass — alpha and width rising toward the body — drawn through
/// <see cref="IDrawContext2D.DrawGradientPolyline"/>.
/// </summary>
/// <remarks>
/// This was a hand-rolled fan of short <see cref="IDrawContext2D.DrawPath"/> calls, one per segment
/// per pass, because the engine had no per-vertex colored polyline. It has one now, so the ramp is
/// stated once per vertex and the engine resolves it per segment. Two visible consequences beyond
/// the deleted loop: the joins abut instead of overlapping, which matters more here than usual
/// because these strokes are additive and an overlap adds twice; and the ramp is sampled at each
/// segment's midpoint rather than at its leading edge, so the streak brightens half a segment later
/// than it used to.
/// </remarks>
internal sealed class OrbitRingNode : Drawable
{
    private const int TrailSegments = 30;
    private const int TrailVertices = TrailSegments + 1;

    private readonly Body body;
    private readonly PathBuilder ring = new(initialCapacity: 14);

    /// <summary>The trail's geometry and both passes' ramps, refilled each frame in place.</summary>
    private readonly Vector2[] trail = new Vector2[TrailVertices];
    private readonly Color[] spillColors = new Color[TrailVertices];
    private readonly float[] spillWidths = new float[TrailVertices];
    private readonly Color[] coreColors = new Color[TrailVertices];
    private readonly float[] coreWidths = new float[TrailVertices];
    private readonly Paint trailTemplate;

    public OrbitRingNode(Body body)
    {
        this.body = body;
        ZIndex = -600;
        ring.AddEllipse(0f, 0f, body.Spec.OrbitRadius, body.Spec.SemiMinorAxis);

        // Everything the two passes share. The color and width come from the ramps, and butt caps
        // at the run's ends as well as its joins keep an additive stroke from ever adding twice.
        trailTemplate = Paint.Stroke(body.Spec.Color, 1f, blendMode: BlendMode.Plus);
    }

    public override Rect VisualBounds =>
        new(
            -body.Spec.OrbitRadius - 4f,
            -body.Spec.SemiMinorAxis - 4f,
            (2f * body.Spec.OrbitRadius) + 8f,
            (2f * body.Spec.SemiMinorAxis) + 8f);

    public override void Render(IDrawContext2D context)
    {
        context.DrawPath(ring, Paint.Stroke(Design.Ring.WithAlpha(0.062f), 1.1f));

        var spec = body.Spec;
        var span = body.TrailTurns * MathF.Tau;
        var scale = MathF.Sqrt(spec.Radius / 12f);

        for (var i = 0; i < TrailVertices; i++)
        {
            var t = i / (float)TrailSegments;
            var angle = body.Angle - (span * (1f - t));
            trail[i] = new Vector2(
                spec.OrbitRadius * MathF.Cos(angle),
                spec.SemiMinorAxis * MathF.Sin(angle));

            // Two passes: a wide, faint one that reads as the light spilling off the streak, and
            // a narrow bright one that reads as the streak itself.
            var ramp = t * t;
            spillColors[i] = spec.Color.WithAlpha(0.085f * ramp);
            spillWidths[i] = (2f + (9f * ramp)) * scale;
            coreColors[i] = spec.Color.WithAlpha(0.34f * ramp);
            coreWidths[i] = (0.5f + (2.4f * ramp)) * scale;
        }

        context.DrawGradientPolyline(trail, spillColors, spillWidths, trailTemplate);
        context.DrawGradientPolyline(trail, coreColors, coreWidths, trailTemplate);
    }
}

/// <summary>
/// A body: an additive halo, a disc lit from the sun's side by an off-center radial gradient, and
/// for one of them a ring in the plane of the ecliptic whose far half is drawn before the disc and
/// whose near half is drawn after.
/// </summary>
internal sealed class PlanetNode : Drawable
{
    private readonly Body body;
    private readonly PathBuilder path = new(initialCapacity: 14);
    private readonly Color lit;
    private readonly Color dark;

    private Vector2 toSun = -Vector2.UnitX;
    private float brightness = 1f;

    public PlanetNode(Body body)
    {
        this.body = body;
        lit = body.Spec.Color.Shade(1.06f, 0.75f);
        dark = body.Spec.Color.Shade(0.42f, 0.85f);
    }

    public override Rect VisualBounds
    {
        get
        {
            var reach = body.Spec.Radius * 6f;
            return new Rect(-reach, -reach, 2f * reach, 2f * reach);
        }
    }

    /// <summary>
    /// Pulls this node's transform and depth cue from the body. The scene calls it, rather than
    /// the node reading the clock in <c>Update</c>, so that the loaded state at <c>t = 0</c> is
    /// already the correct frame: <c>Update</c> never runs for a still exported at zero.
    /// </summary>
    public void Sync()
    {
        Position = body.Position;

        // Near bodies read larger and brighter. This is the whole depth cue, together with the
        // paint order below.
        var depth = body.Depth;
        Scale = new Vector2(1f + (0.10f * depth));
        brightness = 0.70f + (0.30f * (0.5f + (0.5f * depth)));

        // Sibling paint order is the only depth buffer there is, so a body in front of the sun
        // sorts above it and one behind it sorts below.
        ZIndex = depth >= 0f ? 200 : -200;

        toSun = body.Position.LengthSquared() > 1f
            ? -Vector2.Normalize(body.Position)
            : -Vector2.UnitX;
    }

    public override void Render(IDrawContext2D context)
    {
        var r = body.Spec.Radius;

        path.Reset();
        path.AddCircle(0f, 0f, r * 5.5f);
        context.DrawPath(
            path,
            Paint.Fill(
                Shapes.Glow(body.Spec.Color, r * 5.5f, 0.16f * brightness),
                blendMode: BlendMode.Plus));

        path.Reset();
        path.AddCircle(0f, 0f, r * 2.2f);
        context.DrawPath(
            path,
            Paint.Fill(
                Shapes.Glow(body.Spec.Color, r * 2.2f, 0.34f * brightness),
                blendMode: BlendMode.Plus));

        if (body.Spec.HasRing)
        {
            DrawRing(context, r, near: false);
        }

        Span<GradientStop> stops =
        [
            new GradientStop(0f, Dim(lit, brightness)),
            new GradientStop(0.55f, Dim(body.Spec.Color, brightness)),
            new GradientStop(1f, Dim(dark, brightness)),
        ];

        path.Reset();
        path.AddCircle(0f, 0f, r);
        context.DrawPath(
            path,
            Paint.Fill(Brush.Radial(toSun * (r * 0.6f), r * 1.85f, stops)));

        if (body.Spec.HasRing)
        {
            DrawRing(context, r, near: true);
        }
    }

    private static Color Dim(Color color, float brightness) =>
        color.Shade(0.72f + (0.28f * brightness), 1f);

    private void DrawRing(IDrawContext2D context, float r, bool near)
    {
        // World space is Y-up and the near half of the ecliptic is the negative-Y half.
        var upper = !near;
        var paintInner = Paint.Stroke(body.Spec.Color.WithAlpha(0.30f * brightness), 4.2f);
        var paintOuter = Paint.Stroke(body.Spec.Color.WithAlpha(0.16f * brightness), 2.2f);

        path.Reset();
        path.AddEllipseHalf(0f, 0f, r * 1.75f, r * 1.75f * Design.Tilt, upper);
        context.DrawPath(path, paintInner);

        path.Reset();
        path.AddEllipseHalf(0f, 0f, r * 2.30f, r * 2.30f * Design.Tilt, upper);
        context.DrawPath(path, paintOuter);
    }
}

/// <summary>A moon, parented to its body so it rides the body's orbit for free.</summary>
internal sealed class MoonNode : Drawable
{
    private readonly Body body;
    private readonly PathBuilder path = new(initialCapacity: 6);
    private readonly Color color;

    public MoonNode(Body body)
    {
        this.body = body;
        color = body.Spec.Color.Shade(1.08f, 0.35f);
    }

    public override Rect VisualBounds
    {
        get
        {
            var reach = body.Spec.MoonRadius * 5f;
            return new Rect(-reach, -reach, 2f * reach, 2f * reach);
        }
    }

    /// <inheritdoc cref="PlanetNode.Sync" />
    public void Sync()
    {
        Position = body.MoonOffset;

        // A node always paints beneath its own children, so a moon behind its body would still
        // draw over the disc. Hide it while it is both behind and overlapping.
        var occluded = body.MoonDepth < 0f &&
            MathF.Abs(body.MoonOffset.X) < body.Spec.Radius + body.Spec.MoonRadius;
        Visible = !occluded;
    }

    public override void Render(IDrawContext2D context)
    {
        var r = body.Spec.MoonRadius;
        path.Reset();
        path.AddCircle(0f, 0f, r * 3.8f);
        context.DrawPath(
            path,
            Paint.Fill(Shapes.Glow(color, r * 3.8f, 0.30f), blendMode: BlendMode.Plus));

        path.Reset();
        path.AddCircle(0f, 0f, r);
        context.DrawPath(path, Paint.Fill(color.WithAlpha(0.9f)));
    }
}

/// <summary>
/// The dust belt filling the gap between the fifth and sixth orbits. Every mote shares one
/// angular rate, so the annulus is uniform and the rigid rotation is invisible; only the
/// individual motes read.
/// </summary>
internal sealed class BeltNode : Drawable
{
    private const int MoteCount = 460;
    private const int Buckets = 4;
    private const int Revolutions = 2;

    private readonly float[] radii = new float[MoteCount];
    private readonly float[] angles = new float[MoteCount];
    private readonly float[] rises = new float[MoteCount];
    private readonly float[] sizes = new float[MoteCount];
    private readonly int[] buckets = new int[MoteCount];
    private readonly PathBuilder[] paths = new PathBuilder[Buckets];

    private float rotation;

    public BeltNode()
    {
        ZIndex = -500;

        var random = new Random(20260817);
        for (var i = 0; i < MoteCount; i++)
        {
            // Two samples averaged: the belt is denser in the middle than at either edge.
            var across = 0.5f * ((float)random.NextDouble() + (float)random.NextDouble());
            radii[i] = 540f + (110f * across);
            angles[i] = (float)(random.NextDouble() * Math.Tau);
            rises[i] = (float)((random.NextDouble() - 0.5d) * 16d);
            sizes[i] = 0.5f + (1.05f * (float)random.NextDouble());
            buckets[i] = random.Next(Buckets);
        }

        for (var i = 0; i < Buckets; i++)
        {
            paths[i] = new PathBuilder(initialCapacity: 6 * MoteCount);
        }
    }

    public override Rect VisualBounds => new(-700f, -280f, 1400f, 560f);

    public void Advance(double loopPhase) =>
        rotation = (float)(Math.Tau * Revolutions * loopPhase);

    public override void Render(IDrawContext2D context)
    {
        foreach (var path in paths)
        {
            path.Reset();
        }

        for (var i = 0; i < MoteCount; i++)
        {
            var angle = angles[i] + rotation;
            var (sin, cos) = MathF.SinCos(angle);
            paths[buckets[i]].AddCircle(
                radii[i] * cos,
                (radii[i] * Design.Tilt * sin) + rises[i],
                sizes[i]);
        }

        for (var i = 0; i < Buckets; i++)
        {
            var alpha = 0.105f + (0.135f * i);
            context.DrawPath(
                paths[i],
                Paint.Fill(Design.Dust.WithAlpha(alpha), blendMode: BlendMode.Plus));
        }
    }
}
