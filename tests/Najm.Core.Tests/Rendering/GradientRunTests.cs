using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

/// <summary>
/// Pins the contract of the two ramped-run conveniences,
/// <see cref="IDrawContext2D.DrawGradientPolyline"/> and
/// <see cref="IDrawContext2D.DrawGradientSpline"/>: how a per-vertex ramp becomes per-segment paint,
/// which cap each segment end gets, and that the geometry is the same geometry the un-ramped
/// spellings emit.
/// </summary>
/// <remarks>
/// <para>
/// These are the one place in Tier 2 where a single convenience call issues many Tier-1 calls, so
/// the per-call breakdown is itself the contract and is asserted rather than assumed. When §7.3's
/// <c>DrawLines(in LineBatch2D)</c> lands and a backend overrides these onto it, the observable
/// facts below — which colors, which widths, which caps, which curve — are what must not change.
/// </para>
/// <para>
/// Every expected color and width is derived from the midpoint rule in the comment above it, never
/// captured from a run.
/// </para>
/// </remarks>
[TestClass]
public sealed class GradientRunTests
{
    private static readonly Vector2[] ThreePoints = [new(0f, 0f), new(10f, 0f), new(10f, 10f)];

    [TestMethod]
    public void APolylineEmitsOneStrokedSegmentPerSpanInOrder()
    {
        var context = new RampRecordingContext();

        context.DrawGradientPolyline(ThreePoints, [], [], Paint.Stroke(Color.White, 2f));

        Assert.HasCount(2, context.Calls, "Three vertices span two segments.");
        AssertSegment(context.Calls[0], ThreePoints[0], ThreePoints[1]);
        AssertSegment(context.Calls[1], ThreePoints[1], ThreePoints[2]);
        Assert.AreEqual(0, context.OtherCallCount, "A ramped run reaches for no other primitive.");

        // Closing adds the span back to the first vertex, and only that.
        var closed = new RampRecordingContext();
        closed.DrawGradientPolyline(ThreePoints, [], [], Paint.Stroke(Color.White, 2f), close: true);

        Assert.HasCount(3, closed.Calls);
        AssertSegment(closed.Calls[2], ThreePoints[2], ThreePoints[0]);
    }

    [TestMethod]
    public void ARunShorterThanOneSegmentPaintsNothingAtAll()
    {
        // Unlike DrawPolyline, which always issues its one Tier-1 call and lets an empty contour
        // paint nothing, a ramped run has no single call to issue — so it issues none.
        var context = new RampRecordingContext();
        var template = Paint.Stroke(Color.White, 1f);

        context.DrawGradientPolyline([], [], [], template);
        context.DrawGradientPolyline([new Vector2(1f, 1f)], [], [], template);
        context.DrawGradientSpline(CatmullRom.Open([new Vector2(1f, 1f)]), [], [], template);

        Assert.IsEmpty(context.Calls);
    }

    [TestMethod]
    public void EachSegmentTakesTheRampValueAtItsOwnMidpoint()
    {
        // Colors: (1,0,0) at α=0.25 and (0,0,1) at α=0.75.
        //   α    = (0.25 + 0.75)/2 = 0.5
        //   R    = (1·0.25 + 0·0.75)/(2·0.5) = 0.25
        //   B    = (0·0.25 + 1·0.75)/(2·0.5) = 0.75
        // The premultiplied mean is the one that keeps a fade toward transparency from dragging the
        // hue: a straight component mean would have given R = B = 0.5, weighting a nearly invisible
        // endpoint as heavily as a nearly opaque one.
        // Widths: (2 + 6)/2 = 4.
        var context = new RampRecordingContext();
        Color[] colors = [new(1f, 0f, 0f, 0.25f), new(0f, 0f, 1f, 0.75f)];
        float[] widths = [2f, 6f];

        context.DrawGradientPolyline(
            [Vector2.Zero, new Vector2(10f, 0f)],
            colors,
            widths,
            Paint.Stroke(Color.White, 1f));

        Assert.HasCount(1, context.Calls);
        var paint = context.Calls[0].Paint;
        Assert.AreEqual(PaintStyle.Stroke, paint.Style, "A ramped run is always stroked.");
        Assert.AreEqual(new Color(0.25f, 0f, 0.75f, 0.5f), paint.Color);
        Assert.AreEqual(4f, paint.StrokeWidth);

        // Equal alphas reduce the premultiplied mean to the plain component mean, which is the case
        // an all-one-hue trail actually hits.
        var flat = new RampRecordingContext();
        flat.DrawGradientPolyline(
            [Vector2.Zero, new Vector2(10f, 0f)],
            [new Color(1f, 0f, 0f, 0.4f), new Color(0f, 1f, 0f, 0.4f)],
            [],
            Paint.Stroke(Color.White, 3f));

        Assert.AreEqual(new Color(0.5f, 0.5f, 0f, 0.4f), flat.Calls[0].Paint.Color);
        Assert.AreEqual(3f, flat.Calls[0].Paint.StrokeWidth, "An empty width ramp takes the template's.");
    }

    [TestMethod]
    public void InteriorJoinsAbutWithButtCapsAndOnlyTheRunsEndsTakeTheTemplateCap()
    {
        // The join rule, which is the whole reason this lives in the engine rather than in every
        // author's Render: two translucent strokes sharing a round cap composite twice over that
        // cap and bead visibly along the run. Only the two ends of an open run have no neighbour to
        // double-blend against, so only they take the caller's cap.
        var context = new RampRecordingContext();
        Vector2[] four = [new(0f, 0f), new(4f, 0f), new(8f, 0f), new(12f, 0f)];

        context.DrawGradientPolyline(four, [], [], Paint.Stroke(Color.White, 2f, cap: LineCap.Round));

        Assert.HasCount(3, context.Calls);
        Assert.AreEqual(LineCap.Round, context.Calls[0].Paint.Cap, "The run's first end is the caller's.");
        Assert.AreEqual(LineCap.Butt, context.Calls[1].Paint.Cap, "Every interior segment abuts.");
        Assert.AreEqual(LineCap.Round, context.Calls[2].Paint.Cap, "The run's last end is the caller's.");

        // A closed run has no ends at all.
        var closed = new RampRecordingContext();
        closed.DrawGradientPolyline(four, [], [], Paint.Stroke(Color.White, 2f, cap: LineCap.Round), close: true);

        Assert.HasCount(4, closed.Calls);
        foreach (var call in closed.Calls)
        {
            Assert.AreEqual(LineCap.Butt, call.Paint.Cap);
        }

        // A one-segment run is both ends at once, so it is entirely the caller's cap.
        var single = new RampRecordingContext();
        single.DrawGradientPolyline(
            [Vector2.Zero, new Vector2(4f, 0f)],
            [],
            [],
            Paint.Stroke(Color.White, 2f, cap: LineCap.Square));

        Assert.AreEqual(LineCap.Square, single.Calls[0].Paint.Cap);
    }

    [TestMethod]
    public void EverythingElseOnTheTemplateRidesEverySegmentUnchanged()
    {
        var dash = new StrokeDash([3f, 2f], phase: 1f);
        var template = Paint.Stroke(
            Color.Srgb(0.2f, 0.4f, 0.6f, 0.8f),
            width: 5f,
            isAntialias: false,
            blendMode: BlendMode.Plus,
            cap: LineCap.Round,
            join: LineJoin.Bevel,
            miterLimit: 2.5f,
            dash: dash);
        var context = new RampRecordingContext();

        context.DrawGradientPolyline(ThreePoints, [], [], template);

        foreach (var call in context.Calls)
        {
            Assert.IsFalse(call.Paint.IsAntialias);
            Assert.AreEqual(BlendMode.Plus, call.Paint.BlendMode);
            Assert.AreEqual(LineJoin.Bevel, call.Paint.Join);
            Assert.AreEqual(2.5f, call.Paint.MiterLimit);
            Assert.AreEqual(dash, call.Paint.Dash);
            Assert.AreEqual(template.Color, call.Paint.Color, "An empty color ramp takes the template's.");
            Assert.AreEqual(5f, call.Paint.StrokeWidth);
        }
    }

    [TestMethod]
    public void AnEmptyColorRampLetsTheTemplatesBrushPaintTheWholeRun()
    {
        // A brush cannot vary per vertex, so the two are alternatives rather than a combination —
        // but a width ramp over a brush is perfectly expressible and is not refused.
        var brush = Brush.Linear(Vector2.Zero, new Vector2(10f, 0f),
        [
            new GradientStop(0f, Color.Black),
            new GradientStop(1f, Color.White),
        ]);
        var context = new RampRecordingContext();

        context.DrawGradientPolyline(ThreePoints, [], [1f, 3f, 5f], Paint.Stroke(brush, 1f));

        Assert.HasCount(2, context.Calls);
        Assert.AreEqual(brush, context.Calls[0].Paint.Brush);
        Assert.AreEqual(2f, context.Calls[0].Paint.StrokeWidth, "(1 + 3)/2.");
        Assert.AreEqual(4f, context.Calls[1].Paint.StrokeWidth, "(3 + 5)/2.");
    }

    [TestMethod]
    public void ASegmentWhoseRampedWidthReachesZeroPaintsNothingRatherThanThrowing()
    {
        // Tapering to a point is the ordinary shape of a trail's tail, and Paint.Stroke refuses a
        // zero width. The segment is dropped instead, so the taper is expressible without the
        // author clamping every ramp by hand.
        var context = new RampRecordingContext();

        context.DrawGradientPolyline(
            ThreePoints,
            [],
            [0f, 0f, 4f],
            Paint.Stroke(Color.White, 1f));

        Assert.HasCount(1, context.Calls, "The first segment averages to zero width and is dropped.");
        Assert.AreEqual(2f, context.Calls[0].Paint.StrokeWidth, "(0 + 4)/2.");
    }

    [TestMethod]
    public void ASplineDrawsExactlyTheCubicsTheUnRampedSpellingWouldHaveDrawn()
    {
        // The seam this builds on: CatmullRomSegments' indexer gives the same cubics
        // AddOpenCatmullRom emits, so a ramped trail and a plain one trace the same curve and can
        // never disagree about where the spline goes.
        var context = new RampRecordingContext();
        Vector2[] points = [new(0f, 0f), new(10f, 4f), new(18f, -2f), new(25f, 6f)];

        context.DrawGradientSpline(CatmullRom.Open(points), [], [], Paint.Stroke(Color.White, 2f));

        var reference = new PathBuilder().AddOpenCatmullRom(points);
        var expected = reference.Commands;
        Assert.HasCount(3, context.Calls, "Four control points span three cubics.");
        Assert.AreEqual(4, expected.Length, "One MoveTo and three CubicTo.");

        for (var index = 0; index < context.Calls.Count; index++)
        {
            var commands = context.Calls[index].Commands;
            Assert.HasCount(2, commands, "Each segment is its own contour: one MoveTo, one CubicTo.");
            Assert.AreEqual(PathVerb.Move, commands[0].Verb);
            Assert.AreEqual(PathVerb.Cubic, commands[1].Verb);

            // The MoveTo repeats the previous cubic's endpoint, which is the whole spline's own
            // point sequence: the reference contour's MoveTo for the first, then each CubicTo's end.
            var start = index == 0 ? expected[0].Point1 : expected[index].Point3;
            Assert.AreEqual(start, commands[0].Point1, $"Segment {index} starts where the spline does.");
            Assert.AreEqual(expected[index + 1].Point1, commands[1].Point1, $"Segment {index} control 1.");
            Assert.AreEqual(expected[index + 1].Point2, commands[1].Point2, $"Segment {index} control 2.");
            Assert.AreEqual(expected[index + 1].Point3, commands[1].Point3, $"Segment {index} end.");
        }
    }

    [TestMethod]
    public void AClosedSplineWrapsItsLastSegmentBackToTheFirstControlPoint()
    {
        var context = new RampRecordingContext();
        Vector2[] points = [new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)];

        context.DrawGradientSpline(CatmullRom.Closed(points), [], [], Paint.Stroke(Color.White, 2f));

        Assert.HasCount(4, context.Calls, "A closed spline has one segment per control point.");
        Assert.AreEqual(points[3], context.Calls[3].Commands[0].Point1);
        Assert.AreEqual(points[0], context.Calls[3].Commands[1].Point3, "The last segment returns home.");
    }

    [TestMethod]
    public void TheControlPointSpellingForwardsToTheSplineItDescribes()
    {
        Vector2[] points = [new(0f, 0f), new(10f, 4f), new(18f, -2f)];
        var template = Paint.Stroke(Color.White, 2f);
        var direct = new RampRecordingContext();
        var sugar = new RampRecordingContext();

        direct.DrawGradientSpline(CatmullRom.Open(points, CatmullRom.ChordalAlpha), [], [], template);
        sugar.DrawGradientSpline(points, [], [], template, closed: false, alpha: CatmullRom.ChordalAlpha);

        Assert.HasCount(direct.Calls.Count, sugar.Calls);
        for (var index = 0; index < direct.Calls.Count; index++)
        {
            CollectionAssert.AreEqual(
                (System.Collections.ICollection)direct.Calls[index].Commands,
                (System.Collections.ICollection)sugar.Calls[index].Commands,
                $"Segment {index} must be the same cubic either way.");
        }

        // And the alpha really is forwarded rather than defaulted underneath. The comparison is on
        // the second segment: the first one's departure tangent reduces to (end - start) whatever
        // the alpha, because its phantom neighbour is reflected through its own start point.
        var centripetal = new RampRecordingContext();
        centripetal.DrawGradientSpline(points, [], [], template);
        Assert.AreNotEqual(
            direct.Calls[1].Commands[1].Point1,
            centripetal.Calls[1].Commands[1].Point1,
            "Chordal and centripetal must not agree on this control polygon, or the test proves nothing.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => DrawContext2DExtensions.DrawGradientSpline(null!, points, [], [], template));
    }

    [TestMethod]
    public void AMismatchedOrUnusableRampIsRefusedBeforeAnythingIsDrawn()
    {
        var context = new RampRecordingContext();
        var template = Paint.Stroke(Color.White, 2f);

        var shortColors = Assert.ThrowsExactly<ArgumentException>(
            () => context.DrawGradientPolyline(ThreePoints, [Color.White, Color.Black], [], template));
        Assert.AreEqual("vertexColors", shortColors.ParamName);
        StringAssert.Contains(shortColors.Message, "3 expected, 2 given");

        var shortWidths = Assert.ThrowsExactly<ArgumentException>(
            () => context.DrawGradientPolyline(ThreePoints, [], [1f, 2f, 3f, 4f], template));
        Assert.AreEqual("vertexWidths", shortWidths.ParamName);

        var negative = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.DrawGradientPolyline(ThreePoints, [], [1f, -2f, 3f], template));
        Assert.AreEqual("vertexWidths", negative.ParamName);

        var infinite = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.DrawGradientPolyline(ThreePoints, [], [1f, float.NaN, 3f], template));
        Assert.AreEqual("vertexWidths", infinite.ParamName);

        // No usable width anywhere: default(Paint) is a fill and carries a zero stroke width, which
        // would otherwise fail one segment at a time inside Paint.Stroke and name a parameter the
        // caller never passed.
        var noWidth = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.DrawGradientPolyline(ThreePoints, [], [], default));
        Assert.AreEqual("template", noWidth.ParamName);

        // A brush and a color ramp are two answers to the same question.
        var brush = Brush.Solid(Color.White);
        var both = Assert.ThrowsExactly<ArgumentException>(
            () => context.DrawGradientPolyline(
                ThreePoints,
                [Color.White, Color.Black, Color.White],
                [],
                Paint.Stroke(brush, 2f)));
        Assert.AreEqual("template", both.ParamName);

        Assert.IsEmpty(context.Calls, "A refused run must not have left half a trail on the surface.");
    }

    [TestMethod]
    public void AWarmRampedRunAllocatesNoManagedBytes()
    {
        // The per-frame case: a trail redrawn every frame from a reused buffer must not allocate,
        // which means the scratch builder is reused across all N segments of one call and the
        // per-segment Paint stays a struct.
        var context = new RampCountingContext();
        var points = new Vector2[16];
        var colors = new Color[16];
        var widths = new float[16];
        for (var index = 0; index < points.Length; index++)
        {
            var t = index / (float)(points.Length - 1);
            points[index] = new Vector2(index * 3f, MathF.Sin(t * 6f) * 20f);
            colors[index] = new Color(1f, 0.4f, 0.1f, t);
            widths[index] = 0.5f + (3f * t);
        }

        var template = Paint.Stroke(Color.White, 1f, cap: LineCap.Round);
        var reading = AllocationProbe.AssertNoneAllocated(
            100,
            () =>
            {
                context.DrawGradientPolyline(points, colors, widths, template);
                context.DrawGradientSpline(CatmullRom.Open(points), colors, widths, template);
            },
            "The warm ramped-run loop");

        // 15 polyline segments plus 15 spline segments per invocation. widths[0] is 0.5, so no
        // segment averages to zero width and none is dropped.
        Assert.AreEqual(reading.Invocations * 30, context.DrawPathCount);
    }

    private static void AssertSegment(RampCall call, Vector2 start, Vector2 end)
    {
        Assert.HasCount(2, call.Commands, "A straight segment is one MoveTo and one LineTo.");
        Assert.AreEqual(PathVerb.Move, call.Commands[0].Verb);
        Assert.AreEqual(start, call.Commands[0].Point1);
        Assert.AreEqual(PathVerb.Line, call.Commands[1].Verb);
        Assert.AreEqual(end, call.Commands[1].Point1);
    }

    /// <summary>One Tier-1 call a ramped run issued.</summary>
    /// <param name="Commands">A copy of the geometry, taken before the scratch builder is reused.</param>
    /// <param name="Paint">The paint that segment was stroked with.</param>
    private sealed record RampCall(IReadOnlyList<PathCommand> Commands, Paint Paint);

    /// <summary>Counts Tier-1 calls without retaining anything, for the allocation probe.</summary>
    private class RampCountingContext : DrawContext2DBase
    {
        internal int DrawPathCount { get; private set; }

        internal int OtherCallCount { get; private set; }

        public override SurfaceSpec SurfaceSpec { get; } = new(64, 64);

        public override RenderCaps Caps => RenderCaps.None;

        public override float RenderScale => 1f;

        public override float Scale => 1f;

        public override void Clear(Color color) => OtherCallCount++;

        public override void DrawPath(PathBuilder path, in Paint paint)
        {
            ArgumentNullException.ThrowIfNull(path);
            DrawPathCount++;
            OnPath(path, paint);
        }

        public override void DrawImage(
            IImage image,
            in Matrix3x2 imageToLocal,
            ImageSampling sampling = ImageSampling.Linear) => OtherCallCount++;

        public override void SetEngineTransform(in Matrix3x2 engineToDevice) => OtherCallCount++;

        public override void BeginLayerBracket(in LayerBracket bracket) => OtherCallCount++;

        public override void EndLayerBracket() => OtherCallCount++;

        public override void BeginUnitBracket(in UnitBracket bracket) => OtherCallCount++;

        public override void EndUnitBracket() => OtherCallCount++;

        public override void BeginClipBracket(in ClipBracket bracket) => OtherCallCount++;

        public override void EndClipBracket() => OtherCallCount++;

        public override void PushTransform(in Matrix3x2 localTransform) => OtherCallCount++;

        public override void PopTransform() => OtherCallCount++;

        public override void PushClip(in Rect bounds) => OtherCallCount++;

        public override void PushClip(PathBuilder path) => OtherCallCount++;

        public override void PopClip() => OtherCallCount++;

        public override void PushOpacity(float opacity) => OtherCallCount++;

        public override void PopOpacity() => OtherCallCount++;

        /// <summary>Hook for the recording subclass; allocation-free when unused.</summary>
        protected virtual void OnPath(PathBuilder path, in Paint paint)
        {
        }
    }

    /// <summary>Keeps every Tier-1 call a ramped run issued, geometry and paint together.</summary>
    private sealed class RampRecordingContext : RampCountingContext
    {
        private readonly List<RampCall> calls = [];

        internal IReadOnlyList<RampCall> Calls => calls;

        protected override void OnPath(PathBuilder path, in Paint paint)
        {
            // Copied, because the next segment reuses the same scratch builder.
            var commands = new List<PathCommand>(path.Count);
            foreach (var command in path.Commands)
            {
                commands.Add(command);
            }

            calls.Add(new RampCall(commands, paint));
        }
    }
}
