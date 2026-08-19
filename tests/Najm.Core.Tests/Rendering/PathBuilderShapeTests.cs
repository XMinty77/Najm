using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

/// <summary>
/// Pins the one geometry implementation behind the Tier-2 conveniences: the arc constant, the
/// quadrant split, the exactness of the axis-aligned cases, and the command shapes.
/// </summary>
[TestClass]
public sealed class PathBuilderShapeTests
{
    /// <summary>The kappa the orrery sample hand-rolled, kept here as the thing to agree with.</summary>
    /// <remarks>
    /// A field rather than a <c>const</c> so the comparison against the engine's own constant is a
    /// real runtime check instead of one the compiler folds away.
    /// </remarks>
    private static readonly float HandRolledKappa = 0.5522847498307936f;

    [TestMethod]
    public void QuarterTurnKappaIsTheDerivedRatio()
    {
        // k = 4/3 · tan(θ/4) at θ = π/2, and tan(π/8) = √2 − 1 exactly, so k = 4·(√2 − 1)/3.
        var derived = 4d * (Math.Sqrt(2d) - 1d) / 3d;

        Assert.AreEqual(
            PathBuilderShapeExtensions.QuarterTurnKappa,
            (float)derived,
            $"The literal must be the derived ratio {derived:R}, not a copied approximation.");
        Assert.AreEqual(
            PathBuilderShapeExtensions.QuarterTurnKappa,
            HandRolledKappa,
            "The engine constant must agree with the hand-rolled one it replaces.");
        Assert.AreEqual(
            PathBuilderShapeExtensions.QuarterTurnKappa,
            (float)(4d / 3d * Math.Tan(Math.PI / 8d)),
            "The closed form and the general 4/3·tan(θ/4) form must agree at a quarter turn.");
    }

    [TestMethod]
    public void QuarterTurnCubicStaysWithinTheKnownRadialError()
    {
        // The unit-circle quadrant cubic is (1,0) → (1,k) → (k,1) → (0,1). Its own midpoint is
        // (P0 + 3P1 + 3P2 + P3)/8 = ((4 + 3k)/8, (4 + 3k)/8), and with k = 4(√2 − 1)/3 that is
        // 3k = 4√2 − 4, so the component is √2/2 and the midpoint sits exactly on the circle.
        // That property is what fixes k; the error elsewhere peaks near t = 0.21 at ~2.7e-4.
        var k = (double)PathBuilderShapeExtensions.QuarterTurnKappa;
        var worst = 0d;
        for (var step = 0; step <= 1000; step++)
        {
            var t = step / 1000d;
            var point = CubicAt(t, (1d, 0d), (1d, k), (k, 1d), (0d, 1d));
            worst = Math.Max(worst, Math.Abs(Math.Sqrt((point.X * point.X) + (point.Y * point.Y)) - 1d));
        }

        var midpoint = CubicAt(0.5d, (1d, 0d), (1d, k), (k, 1d), (0d, 1d));
        Assert.AreEqual(
            Math.Sqrt(2d) / 2d,
            midpoint.X,
            1e-7d,
            "The quarter-turn cubic's own midpoint must land on the circle; that is what fixes kappa.");
        Assert.IsLessThan(
            3e-4d,
            worst,
            $"The quarter-turn approximation must stay inside its documented 2.7e-4 bound; saw {worst:R}.");
    }

    [TestMethod]
    public void HalfTurnInOneCubicWouldBeFarWorse_WhichIsWhyArcsSplit()
    {
        // Same construction over a half turn: k = 4/3·tan(π/4) = 4/3, endpoints (1,0) and (−1,0),
        // control points (1, 4/3) and (−1, 4/3). The error grows roughly as sweep⁶.
        var k = 4d / 3d;
        var worst = 0d;
        for (var step = 0; step <= 1000; step++)
        {
            var t = step / 1000d;
            var point = CubicAt(t, (1d, 0d), (1d, k), (-1d, k), (-1d, 0d));
            worst = Math.Max(worst, Math.Abs(Math.Sqrt((point.X * point.X) + (point.Y * point.Y)) - 1d));
        }

        Assert.IsGreaterThan(
            0.01d,
            worst,
            $"A one-cubic half turn must be visibly wrong, justifying the quadrant split; saw {worst:R}.");
    }

    [TestMethod]
    public void AddEllipseMatchesTheHandRolledQuadrantConstructionBitForBit()
    {
        // The construction the orrery sample wrote by hand against the public API, verbatim in
        // shape: start at (cx + rx, cy) and turn toward +y through four quarter-turn cubics.
        const float CenterX = 37.5f;
        const float CenterY = -12.25f;
        const float RadiusX = 19f;
        const float RadiusY = 6.5f;
        var offsetX = RadiusX * HandRolledKappa;
        var offsetY = RadiusY * HandRolledKappa;
        var handRolled = new PathBuilder()
            .MoveTo(CenterX + RadiusX, CenterY)
            .CubicTo(CenterX + RadiusX, CenterY + offsetY, CenterX + offsetX, CenterY + RadiusY, CenterX, CenterY + RadiusY)
            .CubicTo(CenterX - offsetX, CenterY + RadiusY, CenterX - RadiusX, CenterY + offsetY, CenterX - RadiusX, CenterY)
            .CubicTo(CenterX - RadiusX, CenterY - offsetY, CenterX - offsetX, CenterY - RadiusY, CenterX, CenterY - RadiusY)
            .CubicTo(CenterX + offsetX, CenterY - RadiusY, CenterX + RadiusX, CenterY - offsetY, CenterX + RadiusX, CenterY)
            .Close();

        var convenience = new PathBuilder()
            .AddEllipse(new Vector2(CenterX, CenterY), new Vector2(RadiusX, RadiusY));

        AssertSameCommands(handRolled, convenience, tolerance: 0f);
    }

    [TestMethod]
    public void AddCircleIsTheIsotropicEllipse()
    {
        var center = new Vector2(4f, -9f);
        var circle = new PathBuilder().AddCircle(center, 3.75f);
        var ellipse = new PathBuilder().AddEllipse(center, new Vector2(3.75f, 3.75f));

        AssertSameCommands(ellipse, circle, tolerance: 0f);
    }

    [TestMethod]
    public void AddEllipseHitsItsFourExtremePointsExactly()
    {
        var center = new Vector2(10f, 20f);
        var radii = new Vector2(7f, 3f);

        var commands = new PathBuilder().AddEllipse(center, radii).Commands;

        Assert.AreEqual(6, commands.Length, "Move, four quadrant cubics, close.");
        Assert.AreEqual(new Vector2(17f, 20f), commands[0].Point1, "Start at center + (rx, 0).");
        Assert.AreEqual(new Vector2(10f, 23f), commands[1].Point3, "Quarter turn reaches center + (0, ry).");
        Assert.AreEqual(new Vector2(3f, 20f), commands[2].Point3, "Half turn reaches center − (rx, 0).");
        Assert.AreEqual(new Vector2(10f, 17f), commands[3].Point3, "Three quarters reaches center − (0, ry).");
        Assert.AreEqual(new Vector2(17f, 20f), commands[4].Point3, "The contour closes on the point it opened with.");
        Assert.AreEqual(PathVerb.Close, commands[5].Verb);
    }

    [TestMethod]
    public void AddArcSplitsBySweepAndNeverExceedsAQuarterTurnPerCubic()
    {
        var center = Vector2.Zero;
        var radii = new Vector2(1f, 1f);

        Assert.AreEqual(
            1,
            CubicCount(new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.Deg(90d))),
            "A 90° sweep is exactly one cubic.");
        Assert.AreEqual(
            2,
            CubicCount(new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.Deg(91d))),
            "A 91° sweep must split rather than widen one cubic past a quarter turn.");
        Assert.AreEqual(
            4,
            CubicCount(new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.FullTurn)),
            "A full turn is four quadrants.");
        Assert.AreEqual(
            3,
            CubicCount(new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.Deg(-200d))),
            "A negative sweep splits on its magnitude.");
    }

    [TestMethod]
    public void AddArcTracksTheCircleAcrossItsWholeSweep()
    {
        // 200° from 30°, on a unit circle at the origin: every flattened sample must sit on the
        // circle to within the per-quadrant bound, which is the point of splitting.
        var path = new PathBuilder().AddArc(Vector2.Zero, new Vector2(1f, 1f), Angle.Deg(30d), Angle.Deg(200d));
        var commands = path.Commands;
        var worst = 0d;
        var start = commands[0].Point1;
        for (var index = 1; index < commands.Length; index++)
        {
            var command = commands[index];
            for (var step = 0; step <= 64; step++)
            {
                var point = CubicAt(
                    step / 64d,
                    (start.X, start.Y),
                    (command.Point1.X, command.Point1.Y),
                    (command.Point2.X, command.Point2.Y),
                    (command.Point3.X, command.Point3.Y));
                worst = Math.Max(worst, Math.Abs(Math.Sqrt((point.X * point.X) + (point.Y * point.Y)) - 1d));
            }

            start = command.Point3;
        }

        Assert.IsLessThan(
            3e-4d,
            worst,
            $"Every split segment must stay inside the quarter-turn bound; saw {worst:R}.");
        Assert.AreEqual(
            Math.Cos(Angle.Deg(230d).Radians),
            commands[^1].Point3.X,
            1e-6d,
            "The arc must end where 30° + 200° says it does.");
        Assert.AreEqual(
            Math.Sin(Angle.Deg(230d).Radians),
            commands[^1].Point3.Y,
            1e-6d,
            "The arc must end where 30° + 200° says it does.");
    }

    [TestMethod]
    public void ArcModeChoosesTheContourShape()
    {
        var center = new Vector2(2f, 3f);
        var radii = new Vector2(4f, 4f);

        var open = new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.QuarterTurn, ArcMode.Open).Commands;
        Assert.AreEqual(2, open.Length, "Open: one move and one cubic.");
        Assert.AreEqual(PathVerb.Cubic, open[^1].Verb, "Open leaves the contour open.");

        var chord = new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.QuarterTurn, ArcMode.Chord).Commands;
        Assert.AreEqual(3, chord.Length, "Chord: move, cubic, close.");
        Assert.AreEqual(new Vector2(6f, 3f), chord[0].Point1, "Chord starts on the arc, not at the center.");
        Assert.AreEqual(PathVerb.Close, chord[^1].Verb);

        var pie = new PathBuilder().AddArc(center, radii, Angle.Zero, Angle.QuarterTurn, ArcMode.Pie).Commands;
        Assert.AreEqual(4, pie.Length, "Pie: move to center, line out, cubic, close.");
        Assert.AreEqual(center, pie[0].Point1, "Pie starts at the center.");
        Assert.AreEqual(PathVerb.Line, pie[1].Verb);
        Assert.AreEqual(new Vector2(6f, 3f), pie[1].Point1, "The radius runs out to the start angle.");
        Assert.AreEqual(PathVerb.Close, pie[^1].Verb);
    }

    [TestMethod]
    public void AddRectRunsClockwiseAndCloses()
    {
        var commands = new PathBuilder().AddRect(new Rect(1f, 2f, 6f, 4f)).Commands;

        Assert.AreEqual(5, commands.Length);
        Assert.AreEqual(new Vector2(1f, 2f), commands[0].Point1);
        Assert.AreEqual(new Vector2(7f, 2f), commands[1].Point1);
        Assert.AreEqual(new Vector2(7f, 6f), commands[2].Point1);
        Assert.AreEqual(new Vector2(1f, 6f), commands[3].Point1);
        Assert.AreEqual(PathVerb.Close, commands[4].Verb);
    }

    [TestMethod]
    public void AddRoundRectClampsRadiiPerAxisAndDegeneratesToARect()
    {
        var bounds = new Rect(0f, 0f, 10f, 4f);

        var clamped = new PathBuilder().AddRoundRect(bounds, new Vector2(50f, 50f)).Commands;
        Assert.AreEqual(
            10,
            clamped.Length,
            "Move, four sides, four corners, close — the sides survive as zero-length lines.");
        Assert.AreEqual(new Vector2(5f, 0f), clamped[0].Point1, "rx clamps to half the width.");
        Assert.AreEqual(new Vector2(10f, 2f), clamped[2].Point3, "ry clamps to half the height.");

        var square = new PathBuilder().AddRoundRect(bounds, new Vector2(0f, 3f)).Commands;
        var plain = new PathBuilder().AddRect(bounds).Commands;
        Assert.AreEqual(plain.Length, square.Length, "A zero radius on either axis must emit a plain rectangle.");
    }

    [TestMethod]
    public void AddRoundRectCornersAreTheSameQuadrantCubicsAsAnEllipse()
    {
        // A rounded rectangle whose radii are exactly half its sides is an ellipse with
        // zero-length sides between the corners. Strip the lines and the curves must coincide.
        var bounds = new Rect(-6f, -4f, 12f, 8f);
        var rounded = new PathBuilder().AddRoundRect(bounds, new Vector2(6f, 4f)).Commands;
        var ellipse = new PathBuilder().AddEllipse(Vector2.Zero, new Vector2(6f, 4f)).Commands;

        var roundedCubics = new List<PathCommand>();
        foreach (var command in rounded)
        {
            if (command.Verb == PathVerb.Cubic)
            {
                roundedCubics.Add(command);
            }
        }

        Assert.HasCount(4, roundedCubics);
        // The rounded rectangle opens at the top edge (top-left corner's end) and the ellipse opens
        // at the +x extreme, so the rectangle's first corner is the ellipse's second quadrant.
        for (var index = 0; index < 4; index++)
        {
            var fromRect = roundedCubics[index];
            var fromEllipse = ellipse[((index + 3) % 4) + 1];
            AssertSamePoint(fromEllipse.Point1, fromRect.Point1, 1e-5f, index, 1);
            AssertSamePoint(fromEllipse.Point2, fromRect.Point2, 1e-5f, index, 2);
            AssertSamePoint(fromEllipse.Point3, fromRect.Point3, 1e-5f, index, 3);
        }
    }

    [TestMethod]
    public void AddPolylineNeedsTwoPointsAndOptionallyCloses()
    {
        var empty = new PathBuilder();
        empty.AddPolyline([]);
        empty.AddPolyline([new Vector2(1f, 1f)]);
        Assert.AreEqual(0, empty.Count, "Fewer than two points must leave no stray open contour behind.");

        Span<Vector2> points = [new(0f, 0f), new(4f, 0f), new(4f, 3f)];
        Assert.AreEqual(3, new PathBuilder().AddPolyline(points).Count);

        var closed = new PathBuilder().AddPolyline(points, close: true).Commands;
        Assert.AreEqual(4, closed.Length);
        Assert.AreEqual(PathVerb.Close, closed[^1].Verb);
    }

    [TestMethod]
    public void AddLineIsOneOpenSegment()
    {
        var commands = new PathBuilder().AddLine(new Vector2(6f, 0f), new Vector2(210f, 0f)).Commands;

        Assert.AreEqual(2, commands.Length);
        Assert.AreEqual(new Vector2(6f, 0f), commands[0].Point1);
        Assert.AreEqual(new Vector2(210f, 0f), commands[1].Point1);
        Assert.AreEqual(PathVerb.Line, commands[1].Verb);
    }

    [TestMethod]
    public void DegenerateShapesEmitGeometryRatherThanThrowing()
    {
        // Zero sizes arrive from data — an animated radius passing through zero, an empty extent —
        // and must produce a path that paints nothing, not an exception mid-frame.
        var center = new Vector2(5f, 5f);
        var zeroCircle = new PathBuilder().AddCircle(center, 0f).Commands;
        Assert.AreEqual(6, zeroCircle.Length, "A zero radius still emits a closed contour.");
        Assert.AreEqual(center, zeroCircle[0].Point1, "It opens on the center.");
        for (var index = 1; index <= 4; index++)
        {
            Assert.AreEqual(center, zeroCircle[index].Point3, $"Quadrant {index} collapses onto the center.");
        }

        var zeroSweep = new PathBuilder().AddArc(Vector2.Zero, Vector2.One, Angle.Deg(45d), Angle.Zero).Commands;
        Assert.AreEqual(2, zeroSweep.Length, "A zero sweep is one degenerate cubic on its start point.");
        Assert.AreEqual(zeroSweep[0].Point1, zeroSweep[1].Point3, "It must not wander off its own start.");

        var manyTurns = new PathBuilder()
            .AddArc(Vector2.Zero, Vector2.One, Angle.Zero, Angle.FullTurn * 2d);
        Assert.AreEqual(8, CubicCount(manyTurns), "Two full turns retrace as eight quadrants.");

        var emptyRect = new PathBuilder().AddRoundRect(default, new Vector2(3f, 3f)).Commands;
        Assert.AreEqual(5, emptyRect.Length, "An empty rectangle clamps its radii to zero and stays a rectangle.");
    }

    [TestMethod]
    public void ShapeHelpersRejectInvalidArguments()
    {
        var path = new PathBuilder();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => path.AddCircle(Vector2.Zero, -1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => path.AddCircle(Vector2.Zero, float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => path.AddEllipse(Vector2.Zero, new Vector2(1f, float.PositiveInfinity)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => path.AddRoundRect(new Rect(0f, 0f, 4f, 4f), new Vector2(-1f, 1f)));
        Assert.ThrowsExactly<ArgumentException>(
            () => path.AddArc(Vector2.Zero, Vector2.One, Angle.Zero, Angle.QuarterTurn, (ArcMode)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => path.AddCircle(new Vector2(float.MaxValue, 0f), float.MaxValue));
        Assert.AreEqual(0, path.Count, "A rejected shape must not leave a partial contour behind.");

        Assert.ThrowsExactly<ArgumentNullException>(() => PathBuilderShapeExtensions.AddRect(null!, default));
    }

    [TestMethod]
    public void ShapeHelpersAllocateNothingOnceTheBuilderIsWarm()
    {
        var path = new PathBuilder(FillRule.NonZero, initialCapacity: 32);
        var center = new Vector2(3f, 4f);
        var radii = new Vector2(5f, 2f);
        var bounds = new Rect(0f, 0f, 8f, 6f);
        var points = new[] { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 3f) };
        var calls = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                path.Reset();
                path.AddCircle(center, 4f);
                path.Reset();
                path.AddEllipse(center, radii);
                path.Reset();
                path.AddRect(bounds);
                path.Reset();
                path.AddRoundRect(bounds, new Vector2(2f, 1f));
                path.Reset();
                path.AddLine(center, radii);
                path.Reset();
                path.AddPolyline(points, close: true);
                path.Reset();
                path.AddArc(center, radii, Angle.Zero, Angle.Deg(200d));
                calls++;
            },
            "Warm shape appending");

        Assert.AreEqual(reading.Invocations, calls, "Every probe invocation must have run the body.");
    }

    private static int CubicCount(PathBuilder path)
    {
        var count = 0;
        foreach (var command in path.Commands)
        {
            if (command.Verb == PathVerb.Cubic)
            {
                count++;
            }
        }

        return count;
    }

    private static (double X, double Y) CubicAt(
        double t,
        (double X, double Y) p0,
        (double X, double Y) p1,
        (double X, double Y) p2,
        (double X, double Y) p3)
    {
        var u = 1d - t;
        var a = u * u * u;
        var b = 3d * u * u * t;
        var c = 3d * u * t * t;
        var d = t * t * t;
        return (
            (a * p0.X) + (b * p1.X) + (c * p2.X) + (d * p3.X),
            (a * p0.Y) + (b * p1.Y) + (c * p2.Y) + (d * p3.Y));
    }

    private static void AssertSameCommands(PathBuilder expected, PathBuilder actual, float tolerance)
    {
        var left = expected.Commands;
        var right = actual.Commands;
        Assert.AreEqual(left.Length, right.Length, "Command counts must match.");
        for (var index = 0; index < left.Length; index++)
        {
            Assert.AreEqual(left[index].Verb, right[index].Verb, $"Verb {index}.");
            AssertSamePoint(left[index].Point1, right[index].Point1, tolerance, index, 1);
            AssertSamePoint(left[index].Point2, right[index].Point2, tolerance, index, 2);
            AssertSamePoint(left[index].Point3, right[index].Point3, tolerance, index, 3);
        }
    }

    private static void AssertSamePoint(Vector2 expected, Vector2 actual, float tolerance, int index, int slot)
    {
        if (tolerance == 0f)
        {
            Assert.AreEqual(expected, actual, $"Command {index} point {slot} must match bit for bit.");
            return;
        }

        Assert.AreEqual(expected.X, actual.X, tolerance, $"Command {index} point {slot} X.");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, $"Command {index} point {slot} Y.");
    }
}
