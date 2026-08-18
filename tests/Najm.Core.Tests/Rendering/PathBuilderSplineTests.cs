using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

/// <summary>
/// The emitter seam between <see cref="CatmullRom"/> and <see cref="PathBuilder"/>. The curve maths
/// are proved in <c>CatmullRomTests</c>; what is proved here is the command sequence the emitter
/// produces from them — one <see cref="PathVerb.Move"/> and one <see cref="PathVerb.Cubic"/> per
/// segment, a <see cref="PathVerb.Close"/> only for a closed spline, and nothing at all for input
/// that describes no curve.
/// </summary>
/// <remarks>
/// The control points below are unevenly spaced on purpose: with equal spacing the centripetal,
/// chordal, and uniform parameterizations coincide, so an emitter that silently dropped or reordered
/// the alpha argument would still match. Every expected control point is computed from
/// <see cref="CatmullRom"/> for the same four neighbours, so this file asserts that the emitter
/// forwards the geometry rather than re-deriving the geometry itself.
/// </remarks>
[TestClass]
public sealed class PathBuilderSplineTests
{
    /// <summary>Four unevenly spaced points: a short hop, a long run, then a short hop back.</summary>
    private static readonly Vector2[] FourPoints =
    [
        new(0f, 0f),
        new(1f, 2f),
        new(9f, 3f),
        new(10f, 0f),
    ];

    [TestMethod]
    public void AnOpenSplineEmitsOneMoveThenOneCubicPerSegment()
    {
        var path = new PathBuilder();

        var returned = path.AddOpenCatmullRom(FourPoints);

        Assert.AreSame(path, returned, "The extension chains on the builder it was handed.");

        // Four control points, three spans between them: Move + three Cubics, and no Close.
        var segments = CatmullRom.Open(FourPoints);
        Assert.AreEqual(3, segments.Count);
        AssertContour(path, segments, closed: false);
    }

    [TestMethod]
    public void AClosedSplineEmitsAWrappingSegmentAndACloseVerb()
    {
        var path = new PathBuilder();

        var returned = path.AddClosedCatmullRom(FourPoints);

        Assert.AreSame(path, returned);

        // A closed spline has one segment per point, the last of which returns to the first.
        var segments = CatmullRom.Closed(FourPoints);
        Assert.AreEqual(4, segments.Count);
        AssertContour(path, segments, closed: true);

        // The wrapping segment genuinely arrives back at the first control point, so the Close verb
        // shuts a contour that is already geometrically shut rather than papering over a gap.
        var commands = path.Commands;
        AssertPoint(FourPoints[0], commands[^2].Point3);
    }

    [TestMethod]
    public void TheAlphaArgumentReachesTheCurveMaths()
    {
        // Uniform and centripetal disagree on unevenly spaced points — that disagreement is why
        // centripetal is the default — so emitting with an explicit alpha must produce the uniform
        // geometry, not the default one.
        var uniform = new PathBuilder().AddOpenCatmullRom(FourPoints, CatmullRom.UniformAlpha);
        var centripetal = new PathBuilder().AddOpenCatmullRom(FourPoints);

        AssertContour(uniform, CatmullRom.Open(FourPoints, CatmullRom.UniformAlpha), closed: false);

        // The comparison is made on the *second* segment. An open spline's end segments reflect a
        // phantom neighbour through the endpoint, which forces the outer knot spacing to equal the
        // inner one, and every alpha then reduces to the same straight departure — so the first
        // cubic coincides for all of them and would prove nothing.
        Assert.AreNotEqual(
            uniform.Commands[2].Point1,
            centripetal.Commands[2].Point1,
            "Uniform and centripetal must not coincide on unevenly spaced interior points.");
    }

    [TestMethod]
    public void ASinglePointLeavesTheBuilderCompletelyUntouched()
    {
        // Not even a MoveTo. A lone sample describes no curve, and a stray open contour left behind
        // here would be picked up by a later Close — silently shutting a contour the author never
        // opened, on geometry they never drew.
        var open = new PathBuilder();
        var closed = new PathBuilder();
        ReadOnlySpan<Vector2> single = [new Vector2(3f, 4f)];

        open.AddOpenCatmullRom(single);
        closed.AddClosedCatmullRom(single);

        Assert.AreEqual(0, open.Count);
        Assert.AreEqual(0, closed.Count);

        // The proof that no contour was opened: Close still fails, exactly as it does on an untouched
        // builder. Were a MoveTo emitted, this would succeed.
        Assert.ThrowsExactly<InvalidOperationException>(() => open.Close());
        Assert.ThrowsExactly<InvalidOperationException>(() => closed.Close());
    }

    [TestMethod]
    public void NoPointsLeaveTheBuilderUntouchedAndDoNotDisturbExistingGeometry()
    {
        var empty = new PathBuilder();

        empty.AddOpenCatmullRom([]);
        empty.AddClosedCatmullRom([]);

        Assert.AreEqual(0, empty.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => empty.Close());

        // A degenerate append onto a builder that already holds a contour must not close, reset, or
        // otherwise touch what is there.
        var occupied = new PathBuilder().MoveTo(1f, 1f).LineTo(2f, 2f);

        occupied.AddOpenCatmullRom([]);
        occupied.AddClosedCatmullRom([new Vector2(5f, 5f)]);

        Assert.AreEqual(2, occupied.Count);
        Assert.AreEqual(PathVerb.Move, occupied.Commands[0].Verb);
        Assert.AreEqual(PathVerb.Line, occupied.Commands[1].Verb);

        // The contour it was holding is still open, so it can still be extended and closed.
        occupied.LineTo(3f, 3f).Close();
        Assert.AreEqual(4, occupied.Count);
    }

    [TestMethod]
    public void TwoPointsProduceTheStraightCubicBetweenThem()
    {
        // With no third point to bend toward, both phantom neighbours are reflections along the same
        // chord, so the cubic's controls sit on the segment: the emitted curve is the straight line
        // from the first point to the second, expressed as one cubic rather than as a LineTo.
        var pair = new[] { new Vector2(2f, 1f), new Vector2(6f, 4f) };
        var path = new PathBuilder().AddOpenCatmullRom(pair);

        var commands = path.Commands;
        Assert.AreEqual(2, commands.Length);
        Assert.AreEqual(PathVerb.Move, commands[0].Verb);
        Assert.AreEqual(PathVerb.Cubic, commands[1].Verb);
        AssertPoint(pair[0], commands[0].Point1);
        AssertPoint(pair[1], commands[1].Point3);

        // Controls at one third and two thirds of the chord is what "straight" means for a cubic.
        AssertPoint(Vector2.Lerp(pair[0], pair[1], 1f / 3f), commands[1].Point1);
        AssertPoint(Vector2.Lerp(pair[0], pair[1], 2f / 3f), commands[1].Point2);
    }

    [TestMethod]
    public void TwoPointsClosedRunOutAlongTheChordAndStraightBack()
    {
        // The degenerate loop CatmullRom.Closed documents: two segments, out and back, plus a Close.
        var pair = new[] { new Vector2(0f, 0f), new Vector2(4f, 0f) };
        var path = new PathBuilder().AddClosedCatmullRom(pair);

        var commands = path.Commands;
        Assert.AreEqual(4, commands.Length);
        Assert.AreEqual(PathVerb.Move, commands[0].Verb);
        Assert.AreEqual(PathVerb.Cubic, commands[1].Verb);
        Assert.AreEqual(PathVerb.Cubic, commands[2].Verb);
        Assert.AreEqual(PathVerb.Close, commands[3].Verb);
        AssertPoint(pair[1], commands[1].Point3);
        AssertPoint(pair[0], commands[2].Point3);
    }

    [TestMethod]
    public void AppendingOntoAnExistingContourStartsANewOneRatherThanContinuingIt()
    {
        // The emitter always begins with a MoveTo, so a spline appended after other geometry is a
        // separate contour and does not draw a joining segment from wherever the pen happened to be.
        var path = new PathBuilder().MoveTo(-5f, -5f).LineTo(-4f, -4f);

        path.AddOpenCatmullRom(FourPoints);

        var commands = path.Commands;
        Assert.AreEqual(2 + 1 + 3, commands.Length);
        Assert.AreEqual(PathVerb.Move, commands[2].Verb);
        AssertPoint(FourPoints[0], commands[2].Point1);
    }

    [TestMethod]
    public void ANullBuilderIsRejectedByBothEmitters()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => PathBuilderSplineExtensions.AddOpenCatmullRom(null!, FourPoints));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => PathBuilderSplineExtensions.AddClosedCatmullRom(null!, FourPoints));
    }

    [TestMethod]
    public void EmittingASplineAllocatesNothingOnceTheBuilderHasCapacity()
    {
        // The point of emitting cubics rather than a sampled polyline is that a path can be rebuilt
        // every frame — a trail behind a moving body, a curve through live samples. That only holds
        // if the rebuild is free: the segments are a ref-struct view over the caller's span, and the
        // builder retains its command array across Reset.
        var path = new PathBuilder(initialCapacity: 16);
        var points = new Vector2[8];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new Vector2(index * index, index % 3);
        }

        AllocationProbe.AssertNoneAllocated(
            10_000,
            () =>
            {
                path.Reset();
                path.AddClosedCatmullRom(points);
            },
            "Emitting a warm Catmull-Rom spline");

        // Eight points closed: Move + eight Cubics + Close, which is 10 commands and fits the
        // capacity reserved above, so no Array.Resize ever ran inside the measured window.
        Assert.AreEqual(10, path.Count);
    }

    /// <summary>
    /// Asserts that a builder holds exactly one <see cref="PathVerb.Move"/> to the spline's first
    /// control point, one <see cref="PathVerb.Cubic"/> per segment carrying that segment's two
    /// controls and its endpoint, and a trailing <see cref="PathVerb.Close"/> only when asked.
    /// </summary>
    private static void AssertContour(PathBuilder path, CatmullRomSegments segments, bool closed)
    {
        var commands = path.Commands;
        Assert.AreEqual(1 + segments.Count + (closed ? 1 : 0), commands.Length);
        Assert.AreEqual(PathVerb.Move, commands[0].Verb);
        AssertPoint(segments.Points[0], commands[0].Point1);

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var command = commands[index + 1];
            Assert.AreEqual(PathVerb.Cubic, command.Verb, $"Segment {index} must be a cubic.");
            AssertPoint(segment.Control1, command.Point1);
            AssertPoint(segment.Control2, command.Point2);
            AssertPoint(segment.End, command.Point3);

            // Each cubic starts where the previous one ended, so the contour is continuous without
            // any intervening MoveTo.
            var previousEnd = index == 0 ? segments.Points[0] : segments[index - 1].End;
            AssertPoint(previousEnd, segment.Start);
        }

        if (closed)
        {
            Assert.AreEqual(PathVerb.Close, commands[^1].Verb);
        }
        else
        {
            Assert.AreNotEqual(PathVerb.Close, commands[^1].Verb, "An open contour must stay open.");
        }
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-5f, $"Expected {expected} but got {actual}.");
        Assert.AreEqual(expected.Y, actual.Y, 1e-5f, $"Expected {expected} but got {actual}.");
    }
}
