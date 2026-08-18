using System.Numerics;
using Najm.Utils;

namespace Najm.Utils.Tests;

[TestClass]
public sealed class CatmullRomTests
{
    private const float Tolerance = 1e-5f;

    [TestMethod]
    public void OpenSplineInterpolatesEveryControlPoint()
    {
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(1f, 2f),
            new(4f, 1f),
            new(5f, 4f),
            new(9f, 3f),
        ];
        var segments = CatmullRom.Open(points);

        Assert.AreEqual(4, segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            AssertClose(points[index], segment.Evaluate(0f), $"segment {index} start");
            AssertClose(points[index + 1], segment.Evaluate(1f), $"segment {index} end");

            // The interpolation property is structural, not an artifact of evaluation: the outer
            // Bézier control points *are* the data points. This is what separates Catmull-Rom from
            // a B-spline, and it is the property a graph depends on.
            Assert.AreEqual(points[index], segment.Start);
            Assert.AreEqual(points[index + 1], segment.End);
        }
    }

    [TestMethod]
    public void ClosedSplineInterpolatesEveryControlPointAndReturnsToTheFirst()
    {
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(3f, 1f),
            new(4f, 4f),
            new(1f, 5f),
        ];
        var segments = CatmullRom.Closed(points);

        Assert.AreEqual(4, segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            AssertClose(points[index], segments[index].Evaluate(0f), $"segment {index} start");
            AssertClose(
                points[(index + 1) % points.Length],
                segments[index].Evaluate(1f),
                $"segment {index} end");
        }

        AssertClose(points[0], segments[^1].End, "loop closes on the first point");
    }

    [TestMethod]
    public void UniformSplineMatchesHandDerivedBezierControlPoints()
    {
        // Control points (0,0), (1,1), (2,0), uniform alpha, so every knot spacing is 1 and the
        // tangent at an interior point is (next - previous) / 2. The open spline reflects a phantom
        // through each end: before = 2*(0,0) - (1,1) = (-1,-1), after = 2*(2,0) - (1,1) = (3,-1).
        //
        // Segment 0 over P0=(-1,-1) P1=(0,0) P2=(1,1) P3=(2,0):
        //   departure = (P2 - P0)/2 = (1,1)          control1 = P1 + departure/3 = (1/3, 1/3)
        //   arrival   = (P3 - P1)/2 = (1,0)          control2 = P2 - arrival/3   = (2/3, 1)
        // Segment 1 over P0=(0,0) P1=(1,1) P2=(2,0) P3=(3,-1):
        //   departure = (P2 - P0)/2 = (1,0)          control1 = P1 + departure/3 = (4/3, 1)
        //   arrival   = (P3 - P1)/2 = (1,-1)         control2 = P2 - arrival/3   = (5/3, 1/3)
        ReadOnlySpan<Vector2> points = [new(0f, 0f), new(1f, 1f), new(2f, 0f)];
        var segments = CatmullRom.Open(points, CatmullRom.UniformAlpha);

        var third = 1f / 3f;
        Assert.AreEqual(2, segments.Count);
        AssertClose(new Vector2(0f, 0f), segments[0].Start, "segment 0 start");
        AssertClose(new Vector2(third, third), segments[0].Control1, "segment 0 control1");
        AssertClose(new Vector2(2f * third, 1f), segments[0].Control2, "segment 0 control2");
        AssertClose(new Vector2(1f, 1f), segments[0].End, "segment 0 end");
        AssertClose(new Vector2(1f, 1f), segments[1].Start, "segment 1 start");
        AssertClose(new Vector2(4f * third, 1f), segments[1].Control1, "segment 1 control1");
        AssertClose(new Vector2(5f * third, third), segments[1].Control2, "segment 1 control2");
        AssertClose(new Vector2(2f, 0f), segments[1].End, "segment 1 end");
    }

    [TestMethod]
    public void CentripetalSegmentMatchesHandDerivedBezierControlPoints()
    {
        // Chosen so the centripetal knot spacings are exact: the chords are 1, 4 and 1 long, and
        // their square roots are 1, 2 and 1.
        //   before=(0,0)  start=(1,0)  end=(1,4)  after=(2,4)
        //   d1 = sqrt(1) = 1,  d2 = sqrt(4) = 2,  d3 = sqrt(1) = 1
        //
        //   departure = ((start-before)/d1 - (end-before)/(d1+d2) + (end-start)/d2) * d2
        //             = ((1,0)/1 - (1,4)/3 + (0,4)/2) * 2
        //             = (1 - 1/3, -4/3 + 2) * 2 = (2/3, 2/3) * 2 = (4/3, 4/3)
        //   control1  = start + departure/3 = (1 + 4/9, 4/9) = (13/9, 4/9)
        //
        //   arrival   = ((end-start)/d2 - (after-start)/(d2+d3) + (after-end)/d3) * d2
        //             = ((0,4)/2 - (1,4)/3 + (1,0)/1) * 2
        //             = (-1/3 + 1, 2 - 4/3) * 2 = (2/3, 2/3) * 2 = (4/3, 4/3)
        //   control2  = end - arrival/3 = (1 - 4/9, 4 - 4/9) = (5/9, 32/9)
        var segment = CatmullRom.ToCubic(
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 4f),
            new Vector2(2f, 4f));

        var ninth = 1f / 9f;
        AssertClose(new Vector2(1f, 0f), segment.Start, "start");
        AssertClose(new Vector2(13f * ninth, 4f * ninth), segment.Control1, "control1");
        AssertClose(new Vector2(5f * ninth, 32f * ninth), segment.Control2, "control2");
        AssertClose(new Vector2(1f, 4f), segment.End, "end");
    }

    [TestMethod]
    public void EvenlySpacedPointsMakeTheAlphaFamilyAgree()
    {
        // Every alpha scales all three knot spacings by the same factor here, and the tangent
        // formula is invariant to that factor. Uneven spacing is the only thing alpha reacts to.
        ReadOnlySpan<Vector2> points = [new(0f, 0f), new(2f, 0f), new(4f, 0f), new(6f, 0f)];
        var uniform = CatmullRom.Open(points, CatmullRom.UniformAlpha);
        var centripetal = CatmullRom.Open(points, CatmullRom.CentripetalAlpha);
        var chordal = CatmullRom.Open(points, CatmullRom.ChordalAlpha);

        for (var index = 0; index < uniform.Count; index++)
        {
            AssertClose(uniform[index].Control1, centripetal[index].Control1, "centripetal control1");
            AssertClose(uniform[index].Control2, centripetal[index].Control2, "centripetal control2");
            AssertClose(uniform[index].Control1, chordal[index].Control1, "chordal control1");
            AssertClose(uniform[index].Control2, chordal[index].Control2, "chordal control2");
        }
    }

    [TestMethod]
    public void UniformCuspsOnUnevenSpacingWhereCentripetalDoesNot()
    {
        // Deliberately uneven: two long chords of 100 around one short chord of 1. Every point has
        // y = 0, so the whole velocity vector is its x component and a sign change in that component
        // is a genuine cusp — the velocity passes through zero — not merely an overshoot.
        //
        // Uniform gives the short segment the tangent its long neighbours deserve:
        //   departure = (101 - 0)/2 = 50.5   control1.x = 100 + 50.5/3 = 116.83
        //   arrival   = (201 - 100)/2 = 50.5 control2.x = 101 - 50.5/3 = 84.17
        // so the control polygon runs forward past 116, back past 84, and forward again to 101, and
        // the curve itself sweeps from x = 96.02 up to x = 104.98 to cover a segment one unit long.
        //
        // Centripetal divides by sqrt(chord) instead: d1 = 10, d2 = 1, d3 = 10, giving
        //   departure = 100/10 - 101/11 + 1 = 1.818   control1.x = 100.606
        //   arrival   = 1 - 101/11 + 100/10 = 1.818   control2.x = 100.394
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(100f, 0f),
            new(101f, 0f),
            new(201f, 0f),
        ];
        var uniform = CatmullRom.Open(points, CatmullRom.UniformAlpha)[1];
        var centripetal = CatmullRom.Open(points, CatmullRom.CentripetalAlpha)[1];

        var uniformReversed = false;
        var uniformFarthest = float.NegativeInfinity;
        var uniformNearest = float.PositiveInfinity;
        var centripetalSlowest = float.PositiveInfinity;
        var centripetalFarthest = float.NegativeInfinity;
        var centripetalNearest = float.PositiveInfinity;
        for (var step = 0; step <= 200; step++)
        {
            var t = step / 200f;

            var uniformVelocity = uniform.Tangent(t);
            Assert.AreEqual(0f, uniformVelocity.Y, "the case is one-dimensional by construction");
            uniformReversed |= uniformVelocity.X < 0f;
            var uniformX = uniform.Evaluate(t).X;
            uniformFarthest = MathF.Max(uniformFarthest, uniformX);
            uniformNearest = MathF.Min(uniformNearest, uniformX);

            var centripetalVelocity = centripetal.Tangent(t);
            Assert.AreEqual(0f, centripetalVelocity.Y, "the case is one-dimensional by construction");
            centripetalSlowest = MathF.Min(centripetalSlowest, centripetalVelocity.X);
            var x = centripetal.Evaluate(t).X;
            centripetalFarthest = MathF.Max(centripetalFarthest, x);
            centripetalNearest = MathF.Min(centripetalNearest, x);
        }

        Assert.IsTrue(
            uniformReversed,
            "uniform parameterization should reverse direction inside the short segment, which is "
            + "the cusp centripetal exists to avoid");
        Assert.IsGreaterThan(
            104f,
            uniformFarthest,
            "uniform parameterization should overshoot far past the segment's own endpoints");
        Assert.IsLessThan(
            97f,
            uniformNearest,
            "uniform parameterization should undershoot far behind the segment's own endpoints");

        Assert.IsGreaterThan(
            0f,
            centripetalSlowest,
            "centripetal parameterization should never reverse direction, so it never cusps");
        Assert.IsLessThan(
            101f + Tolerance,
            centripetalFarthest,
            "centripetal parameterization should stay inside the segment it is drawing");
        Assert.IsGreaterThan(
            100f - Tolerance,
            centripetalNearest,
            "centripetal parameterization should stay inside the segment it is drawing");
    }

    [TestMethod]
    public void UniformOvershootsTheSegmentBoundsWhereCentripetalStaysInside()
    {
        // The same failure in two dimensions: a short chord between two long ones. Uniform pushes
        // control1 to x = 11.75, well outside the segment's own [10, 10.5] span; centripetal keeps
        // it at x = 10.40.
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(10f, 0f),
            new(10.5f, 1f),
            new(20f, 1f),
        ];
        var uniform = CatmullRom.Open(points, CatmullRom.UniformAlpha)[1];
        var centripetal = CatmullRom.Open(points, CatmullRom.CentripetalAlpha)[1];

        Assert.IsGreaterThan(11.5f, uniform.Control1.X, "uniform control1 escapes the segment");
        Assert.IsLessThan(
            10.5f + Tolerance,
            centripetal.Control1.X,
            "centripetal control1 stays within the segment");

        for (var step = 0; step <= 200; step++)
        {
            var point = centripetal.Evaluate(step / 200f);
            Assert.IsGreaterThan(10f - Tolerance, point.X, "centripetal curve stays in bounds");
            Assert.IsLessThan(10.5f + Tolerance, point.X, "centripetal curve stays in bounds");
            Assert.IsGreaterThan(0f - Tolerance, point.Y, "centripetal curve stays in bounds");
            Assert.IsLessThan(1f + Tolerance, point.Y, "centripetal curve stays in bounds");
        }
    }

    [TestMethod]
    public void ClosedLoopIsContinuousAcrossTheSeam()
    {
        // Irregularly spaced on purpose, so the seam is not smooth by symmetry.
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(3.5f, 0.5f),
            new(4f, 3f),
            new(1.5f, 4.5f),
            new(-1f, 2f),
        ];
        var segments = CatmullRom.Closed(points);

        for (var joint = 0; joint < points.Length; joint++)
        {
            var incoming = segments[(joint + points.Length - 1) % points.Length];
            var outgoing = segments[joint];
            var arriving = incoming.Tangent(1f);
            var leaving = outgoing.Tangent(0f);

            // Direction is continuous: the two velocities are parallel and point the same way.
            var arrivingDirection = Vector2.Normalize(arriving);
            var leavingDirection = Vector2.Normalize(leaving);
            Assert.AreEqual(
                1f,
                Vector2.Dot(arrivingDirection, leavingDirection),
                1e-4f,
                $"tangent direction is continuous at joint {joint}");

            // And the magnitudes differ only by the knot spacing each segment was parameterized
            // over, so the derivative with respect to the knot parameter matches exactly: that is
            // C1, not merely G1. The seam (joint 0) is held to the same standard as the rest.
            var arrivingSpacing = KnotSpacing(
                points[(joint + points.Length - 1) % points.Length],
                points[joint]);
            var leavingSpacing = KnotSpacing(points[joint], points[(joint + 1) % points.Length]);
            AssertClose(
                arriving / arrivingSpacing,
                leaving / leavingSpacing,
                $"knot-parameter derivative is continuous at joint {joint}",
                1e-3f);
        }
    }

    [TestMethod]
    public void FewerThanTwoPointsProduceNoSegments()
    {
        Assert.AreEqual(0, CatmullRom.Open([]).Count);
        Assert.AreEqual(0, CatmullRom.Closed([]).Count);
        Assert.AreEqual(0, CatmullRom.Open([new Vector2(3f, 4f)]).Count);
        Assert.AreEqual(0, CatmullRom.Closed([new Vector2(3f, 4f)]).Count);
    }

    [TestMethod]
    public void TwoPointsProduceTheStraightCubicBetweenThem()
    {
        // The reflected phantoms are collinear with the pair and evenly spaced, so both endpoints
        // get a tangent of exactly (end - start) and the controls land on the thirds of the chord.
        ReadOnlySpan<Vector2> points = [new(1f, 2f), new(4f, 8f)];

        foreach (var alpha in AllAlphas())
        {
            var segments = CatmullRom.Open(points, alpha);

            Assert.AreEqual(1, segments.Count);
            AssertClose(new Vector2(1f, 2f), segments[0].Start, $"alpha {alpha} start");
            AssertClose(new Vector2(2f, 4f), segments[0].Control1, $"alpha {alpha} control1");
            AssertClose(new Vector2(3f, 6f), segments[0].Control2, $"alpha {alpha} control2");
            AssertClose(new Vector2(4f, 8f), segments[0].End, $"alpha {alpha} end");
        }
    }

    [TestMethod]
    public void ClosedTwoPointLoopRunsOutAlongTheChordAndStraightBack()
    {
        ReadOnlySpan<Vector2> points = [new(0f, 0f), new(6f, 0f)];

        foreach (var alpha in AllAlphas())
        {
            var segments = CatmullRom.Closed(points, alpha);

            Assert.AreEqual(2, segments.Count);
            AssertClose(new Vector2(0f, 0f), segments[0].Control1, $"alpha {alpha} out control1");
            AssertClose(new Vector2(6f, 0f), segments[0].Control2, $"alpha {alpha} out control2");
            AssertClose(new Vector2(6f, 0f), segments[1].Control1, $"alpha {alpha} back control1");
            AssertClose(new Vector2(0f, 0f), segments[1].Control2, $"alpha {alpha} back control2");
        }
    }

    [TestMethod]
    public void ACoincidentSegmentCollapsesInsteadOfLooping()
    {
        // A repeated sample is what a stalled simulation writes into a trail buffer. The segment
        // across the repeat must stay put: given tangents it would draw a small loop out of a
        // zero-length span.
        ReadOnlySpan<Vector2> points = [new(0f, 0f), new(2f, 3f), new(2f, 3f), new(5f, 1f)];

        foreach (var alpha in AllAlphas())
        {
            var segments = CatmullRom.Open(points, alpha);
            var collapsed = segments[1];

            Assert.AreEqual(3, segments.Count);
            AssertClose(new Vector2(2f, 3f), collapsed.Start, $"alpha {alpha} start");
            AssertClose(new Vector2(2f, 3f), collapsed.Control1, $"alpha {alpha} control1");
            AssertClose(new Vector2(2f, 3f), collapsed.Control2, $"alpha {alpha} control2");
            AssertClose(new Vector2(2f, 3f), collapsed.End, $"alpha {alpha} end");
        }
    }

    [TestMethod]
    public void CoincidentNeighboursDoNotDivideByZero()
    {
        // Duplicates at both ends and in the middle of the run: the alpha parameterization divides
        // by each chord length raised to alpha, so every one of these is a division by zero unless
        // the spacing is borrowed.
        ReadOnlySpan<Vector2> points =
        [
            new(0f, 0f),
            new(0f, 0f),
            new(4f, 1f),
            new(4f, 1f),
            new(7f, 5f),
            new(7f, 5f),
        ];

        foreach (var alpha in AllAlphas())
        {
            foreach (var closed in new[] { false, true })
            {
                var segments = closed
                    ? CatmullRom.Closed(points, alpha)
                    : CatmullRom.Open(points, alpha);

                for (var index = 0; index < segments.Count; index++)
                {
                    var segment = segments[index];
                    AssertFinite(segment.Start, $"alpha {alpha} closed {closed} segment {index}");
                    AssertFinite(segment.Control1, $"alpha {alpha} closed {closed} segment {index}");
                    AssertFinite(segment.Control2, $"alpha {alpha} closed {closed} segment {index}");
                    AssertFinite(segment.End, $"alpha {alpha} closed {closed} segment {index}");
                }
            }
        }
    }

    [TestMethod]
    public void TheEnumeratorAgreesWithTheIndexer()
    {
        ReadOnlySpan<Vector2> points = [new(0f, 0f), new(1f, 3f), new(5f, 2f), new(6f, 6f)];
        var segments = CatmullRom.Open(points);
        var index = 0;

        foreach (var segment in segments)
        {
            Assert.AreEqual(segments[index], segment, $"segment {index}");
            index++;
        }

        Assert.AreEqual(segments.Count, index);
    }

    [TestMethod]
    public void SegmentIndicesOutsideTheSplineFailLoudly()
    {
        Vector2[] points = [new(0f, 0f), new(1f, 3f), new(5f, 2f)];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Open(points)[-1]; });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Open(points)[2]; });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Closed(points)[3]; });
    }

    [TestMethod]
    public void NonFiniteControlPointsFailLoudly()
    {
        Vector2[] withNaN = [new(0f, 0f), new(float.NaN, 1f), new(2f, 0f)];
        Vector2[] withInfinity = [new(0f, 0f), new(1f, float.PositiveInfinity), new(2f, 0f)];

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Open(withNaN); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Closed(withNaN); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = CatmullRom.Open(withInfinity); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = CatmullRom.ToCubic(
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, float.NegativeInfinity),
                new Vector2(3f, 0f));
        });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => { _ = new CubicSegment(default, default, default, new Vector2(float.NaN, 0f)); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = CatmullRom.Open([new Vector2(0f, 0f), new Vector2(1f, 1f)])[0].Evaluate(float.NaN);
        });
    }

    [TestMethod]
    public void AlphaOutsideTheFamilyFailsLoudly()
    {
        Vector2[] points = [new(0f, 0f), new(1f, 1f), new(2f, 0f)];

        foreach (var alpha in new[] { -0.001f, 1.001f, float.NaN, float.PositiveInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => { _ = CatmullRom.Open(points, alpha); },
                $"alpha {alpha}");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => { _ = CatmullRom.Closed(points, alpha); },
                $"alpha {alpha}");
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => { _ = CatmullRom.ToCubic(points[0], points[1], points[2], points[0], alpha); },
                $"alpha {alpha}");
        }
    }

    [TestMethod]
    public void AWarmSplineTraversalAllocatesNoManagedMemory()
    {
        var points = new Vector2[64];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new Vector2(index * 0.37f, MathF.Sin(index * 0.41f) * 4f);
        }

        var accumulator = Vector2.Zero;
        var invocations = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            2_000,
            () =>
            {
                foreach (var segment in CatmullRom.Open(points))
                {
                    accumulator += segment.Control1 + segment.Control2;
                }

                foreach (var segment in CatmullRom.Closed(points))
                {
                    accumulator += segment.Control1 + segment.Control2;
                }

                invocations++;
            },
            "Catmull-Rom segment traversal");

        Assert.AreEqual(reading.Invocations, invocations);
        Assert.IsGreaterThan(0f, accumulator.LengthSquared());
    }

    private static float[] AllAlphas() =>
        [CatmullRom.UniformAlpha, CatmullRom.CentripetalAlpha, CatmullRom.ChordalAlpha];

    private static float KnotSpacing(Vector2 from, Vector2 to) =>
        MathF.Sqrt(Vector2.Distance(from, to));

    private static void AssertFinite(Vector2 point, string what)
    {
        Assert.IsTrue(
            float.IsFinite(point.X) && float.IsFinite(point.Y),
            $"{what} produced the non-finite point {point}.");
    }

    private static void AssertClose(
        Vector2 expected,
        Vector2 actual,
        string what,
        float tolerance = Tolerance)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance, $"{what}: x of {actual} vs {expected}");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, $"{what}: y of {actual} vs {expected}");
    }
}
