using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Proves the Tier-2 conveniences are conveniences over Tier 1 and not a second geometry path: each
/// one rasterizes to bytes identical to the explicit <see cref="PathBuilder"/> construction an author
/// would otherwise hand-roll.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The tolerance is zero, and that is a claim, not an accident.</strong> The conveniences
/// call the same <see cref="IDrawContext2D.DrawPath"/> the explicit form does, over control points
/// whose float expressions reduce to the same operations (<c>c ± r</c> and <c>c ± r·κ</c>), so the
/// two paths are bit-identical before they reach Skia and Skia is deterministic on identical input.
/// Anything less than exact equality here would mean the convenience had acquired geometry of its
/// own — which is exactly the failure this file exists to catch. That is also why
/// <see cref="SkiaDrawContext2D"/> declines §7.2's licence to override <c>DrawCircle</c> onto
/// <c>SKCanvas.DrawCircle</c>: a native oval would break this equality on purpose.
/// </para>
/// <para>
/// Antialiasing is left on. A hard-edged comparison would pass on geometry that differs by a
/// fraction of a pixel; coverage-antialiased edges turn a sub-pixel control-point difference into a
/// differing byte, which is what makes the assertion worth making.
/// </para>
/// </remarks>
[TestClass]
public sealed class Tier2ConvenienceGoldenTests
{
    /// <summary>The classic quarter-turn ratio, spelled as an author outside the engine would.</summary>
    private const float Kappa = 0.5522847498307936f;

    private const int Size = 64;

    [TestMethod]
    public void DrawCircleIsPixelIdenticalToTheHandRolledCubicApproximation()
    {
        var center = new Vector2(31.5f, 32.25f);
        const float Radius = 24.75f;

        var convenience = Render(context => context.DrawCircle(center, Radius, Fill));
        var handRolled = Render(context => context.DrawPath(
            HandRolledEllipse(center.X, center.Y, Radius, Radius),
            Fill));

        AssertIdentical(handRolled, convenience, "DrawCircle");
    }

    [TestMethod]
    public void DrawEllipseIsPixelIdenticalToTheHandRolledCubicApproximation()
    {
        var center = new Vector2(32f, 31.75f);
        const float RadiusX = 29.5f;
        const float RadiusY = 13.125f;

        var convenience = Render(context => context.DrawEllipse(center, new Vector2(RadiusX, RadiusY), Fill));
        var handRolled = Render(context => context.DrawPath(
            HandRolledEllipse(center.X, center.Y, RadiusX, RadiusY),
            Fill));

        AssertIdentical(handRolled, convenience, "DrawEllipse");
    }

    [TestMethod]
    public void StrokedCircleIsPixelIdenticalToTheHandRolledCubicApproximation()
    {
        // A stroke exercises the geometry twice — once per offset side — so a control point that
        // differed would show on both edges of the ring rather than only on the fill boundary.
        var center = new Vector2(32f, 32f);
        const float Radius = 22f;
        var stroke = Paint.Stroke(Color.Srgb(0.1f, 0.9f, 0.4f), width: 5.5f);

        var convenience = Render(context => context.DrawCircle(center, Radius, stroke));
        var handRolled = Render(context => context.DrawPath(
            HandRolledEllipse(center.X, center.Y, Radius, Radius),
            stroke));

        AssertIdentical(handRolled, convenience, "A stroked DrawCircle");
    }

    [TestMethod]
    public void DrawRectAndDrawLineArePixelIdenticalToTheirExplicitPaths()
    {
        var bounds = new Rect(6.25f, 9.5f, 41.5f, 30.75f);
        var start = new Vector2(4f, 55.5f);
        var end = new Vector2(60f, 12.25f);
        var stroke = Paint.Stroke(Color.Srgb(1f, 0.2f, 0.6f), width: 3f, cap: LineCap.Round);

        var rectConvenience = Render(context => context.DrawRect(bounds, Fill));
        var rectExplicit = Render(context => context.DrawPath(
            new PathBuilder()
                .MoveTo(bounds.Left, bounds.Top)
                .LineTo(bounds.Right, bounds.Top)
                .LineTo(bounds.Right, bounds.Bottom)
                .LineTo(bounds.Left, bounds.Bottom)
                .Close(),
            Fill));
        AssertIdentical(rectExplicit, rectConvenience, "DrawRect");

        var lineConvenience = Render(context => context.DrawLine(start, end, stroke));
        var lineExplicit = Render(context => context.DrawPath(
            new PathBuilder().MoveTo(start.X, start.Y).LineTo(end.X, end.Y),
            stroke));
        AssertIdentical(lineExplicit, lineConvenience, "DrawLine");
    }

    [TestMethod]
    public void DrawRoundRectIsPixelIdenticalToTheHandRolledCorners()
    {
        var bounds = new Rect(5f, 7.5f, 50f, 44f);
        const float RadiusX = 12f;
        const float RadiusY = 8.5f;
        var offsetX = RadiusX * Kappa;
        var offsetY = RadiusY * Kappa;
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        var convenience = Render(context =>
            context.DrawRoundRect(bounds, new Vector2(RadiusX, RadiusY), Fill));
        var handRolled = Render(context => context.DrawPath(
            new PathBuilder()
                .MoveTo(left + RadiusX, top)
                .LineTo(right - RadiusX, top)
                .CubicTo(right - RadiusX + offsetX, top, right, top + RadiusY - offsetY, right, top + RadiusY)
                .LineTo(right, bottom - RadiusY)
                .CubicTo(right, bottom - RadiusY + offsetY, right - RadiusX + offsetX, bottom, right - RadiusX, bottom)
                .LineTo(left + RadiusX, bottom)
                .CubicTo(left + RadiusX - offsetX, bottom, left, bottom - RadiusY + offsetY, left, bottom - RadiusY)
                .LineTo(left, top + RadiusY)
                .CubicTo(left, top + RadiusY - offsetY, left + RadiusX - offsetX, top, left + RadiusX, top)
                .Close(),
            Fill));

        AssertIdentical(handRolled, convenience, "DrawRoundRect");
    }

    [TestMethod]
    public void DrawArcHalfSweepIsPixelIdenticalToTheHandRolledEllipseHalf()
    {
        // The orrery sample's AddEllipseHalf: the upper half of an ellipse as two quarter-turn
        // cubics. The convenience reaches the same points through the general split.
        var center = new Vector2(32f, 32f);
        const float RadiusX = 27f;
        const float RadiusY = 16f;
        var offsetX = RadiusX * Kappa;
        var offsetY = RadiusY * Kappa;
        var stroke = Paint.Stroke(Color.Srgb(0.9f, 0.85f, 0.2f), width: 2.5f);

        var convenience = Render(context => context.DrawArc(
            center,
            new Vector2(RadiusX, RadiusY),
            Angle.Zero,
            Angle.HalfTurn,
            ArcMode.Open,
            stroke));
        var handRolled = Render(context => context.DrawPath(
            new PathBuilder()
                .MoveTo(center.X + RadiusX, center.Y)
                .CubicTo(
                    center.X + RadiusX,
                    center.Y + offsetY,
                    center.X + offsetX,
                    center.Y + RadiusY,
                    center.X,
                    center.Y + RadiusY)
                .CubicTo(
                    center.X - offsetX,
                    center.Y + RadiusY,
                    center.X - RadiusX,
                    center.Y + offsetY,
                    center.X - RadiusX,
                    center.Y),
            stroke));

        // Not bit-identical geometry: the general arc takes its quadrant endpoints from
        // Math.SinCos, so cos(π/2) arrives as 6.1e-17 rather than 0 and the half-way point sits
        // 27 · 6.1e-17 ≈ 1.7e-15 local units off the axis. That is fourteen orders of magnitude
        // below the 1/256 of a pixel a coverage byte can resolve, so the rasterization is identical
        // even though the floats are not.
        AssertIdentical(handRolled, convenience, "A half-turn DrawArc");
    }

    [TestMethod]
    public void DrawPolylineIsPixelIdenticalToTheExplicitContour()
    {
        Vector2[] points =
        [
            new(8f, 52f), new(20f, 14.5f), new(31f, 40f), new(44.5f, 9f), new(57f, 48.25f),
        ];
        var stroke = Paint.Stroke(Color.Srgb(0.2f, 0.7f, 1f), width: 4f, join: LineJoin.Round);

        var convenience = Render(context => context.DrawPolyline(points, stroke, close: true));
        var explicitPath = Render(context =>
        {
            var path = new PathBuilder().MoveTo(points[0].X, points[0].Y);
            for (var index = 1; index < points.Length; index++)
            {
                path.LineTo(points[index].X, points[index].Y);
            }

            context.DrawPath(path.Close(), stroke);
        });

        AssertIdentical(explicitPath, convenience, "DrawPolyline");
    }

    [TestMethod]
    public void ConveniencesPaintSomething()
    {
        // A guard against the equality tests above passing because both sides drew nothing.
        var convenience = Render(context => context.DrawCircle(new Vector2(32f, 32f), 24f, Fill));
        var covered = 0;
        for (var index = 3; index < convenience.Length; index += 4)
        {
            if (convenience[index] != 0)
            {
                covered++;
            }
        }

        // A radius-24 disc covers π·24² ≈ 1810 pixels; antialiasing adds the partially covered rim,
        // so anything near that number confirms the comparison had real content to compare.
        Assert.IsGreaterThan(1_700, covered, "The comparison must be made over a disc that actually painted.");
        Assert.IsLessThan(2_000, covered, "And that disc must not have flooded the surface.");
    }

    [TestMethod]
    public void WarmConvenienceLoopOnTheSkiaContextAllocatesNoManagedBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(16, 16));
        var context = target.GetContext();
        var center = new Vector2(8f, 8f);
        var radii = new Vector2(6f, 3f);
        var bounds = new Rect(1f, 1f, 14f, 14f);
        var corners = new Vector2(3f, 2f);
        var points = new[] { new Vector2(1f, 1f), new Vector2(14f, 3f), new Vector2(7f, 15f) };
        var stroke = Paint.Stroke(Color.Srgb(1f, 1f, 1f), width: 1.5f);

        var reading = AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                context.DrawCircle(center, 5f, Fill);
                context.DrawEllipse(center, radii, Fill);
                context.DrawRect(bounds, Fill);
                context.DrawRoundRect(bounds, corners, Fill);
                context.DrawLine(center, radii, stroke);
                context.DrawPolyline(points, stroke, close: true);
                context.DrawArc(center, radii, Angle.Zero, Angle.Deg(200d), ArcMode.Pie, Fill);
            },
            "The warm Tier-2 loop over the Skia context");

        Assert.IsGreaterThan(
            0,
            reading.Invocations,
            "The probe must have run the body, or the reading proves nothing.");
    }

    private static Paint Fill => Paint.Fill(Color.Srgb(0.15f, 0.55f, 0.95f));

    /// <summary>The hand-rolled ellipse from the orrery sample's <c>Shapes.cs</c>, verbatim in form.</summary>
    private static PathBuilder HandRolledEllipse(float cx, float cy, float rx, float ry)
    {
        var ox = rx * Kappa;
        var oy = ry * Kappa;
        return new PathBuilder()
            .MoveTo(cx + rx, cy)
            .CubicTo(cx + rx, cy + oy, cx + ox, cy + ry, cx, cy + ry)
            .CubicTo(cx - ox, cy + ry, cx - rx, cy + oy, cx - rx, cy)
            .CubicTo(cx - rx, cy - oy, cx - ox, cy - ry, cx, cy - ry)
            .CubicTo(cx + ox, cy - ry, cx + rx, cy - oy, cx + rx, cy)
            .Close();
    }

    private static byte[] Render(Action<IDrawContext2D> draw)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(Size, Size));
        var context = target.GetContext();
        context.Clear(Color.Transparent);
        draw(context);

        using var snapshot = target.Snapshot();
        var pixels = new byte[Size * Size * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static void AssertIdentical(byte[] expected, byte[] actual, string what)
    {
        var differing = 0;
        var worst = 0;
        var firstIndex = -1;
        for (var index = 0; index < expected.Length; index++)
        {
            var delta = Math.Abs(expected[index] - actual[index]);
            if (delta == 0)
            {
                continue;
            }

            differing++;
            worst = Math.Max(worst, delta);
            if (firstIndex < 0)
            {
                firstIndex = index;
            }
        }

        Assert.AreEqual(
            0,
            differing,
            $"{what} must rasterize exactly as the explicit Tier-1 path does. {differing} of "
            + $"{expected.Length} bytes differ, worst by {worst}, first at byte {firstIndex} "
            + $"(pixel {(firstIndex < 0 ? -1 : firstIndex / 4)}, channel "
            + $"{(firstIndex < 0 ? -1 : firstIndex % 4)}).");
    }
}
