using System.Numerics;
using Najm.Utils;
using Najm.Core.Text;

namespace Najm.Core.Tests.Rendering;

/// <summary>
/// Pins the Tier-2 contract that <see cref="DrawContext2DBase"/> exists to hold: one Tier-1
/// <see cref="IDrawContext2D.DrawPath"/> per convenience, one reused scratch builder, no allocation,
/// and a re-entrancy guard that fails loudly instead of corrupting geometry.
/// </summary>
[TestClass]
public sealed class DrawContext2DBaseTests
{
    [TestMethod]
    public void EveryConvenienceIssuesExactlyOneTierOneDrawPath()
    {
        var context = new RecordingContext();

        DrawEveryConvenience(context);

        Assert.AreEqual(
            7,
            context.DrawPathCount,
            "Seven conveniences must lower to seven DrawPath calls and nothing else.");
        Assert.AreEqual(0, context.OtherCallCount, "A convenience must not reach for any other primitive.");
    }

    [TestMethod]
    public void ConvenienceGeometryIsTheSameAsTheExplicitTierOneSpelling()
    {
        var context = new RecordingContext();
        var center = new Vector2(11f, -3f);
        var radii = new Vector2(5f, 2.5f);
        var bounds = new Rect(2f, 4f, 9f, 6f);
        var corners = new Vector2(1.5f, 1f);
        Vector2[] points = [new(0f, 0f), new(3f, 1f), new(6f, -2f)];

        context.DrawCircle(center, 4f, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddCircle(center, 4f));

        context.DrawEllipse(center, radii, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddEllipse(center, radii));

        context.DrawRect(bounds, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddRect(bounds));

        context.DrawRoundRect(bounds, corners, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddRoundRect(bounds, corners));

        context.DrawLine(center, radii, Paint.Stroke(Color.White, 1f));
        AssertLastPathIs(context, new PathBuilder().AddLine(center, radii));

        context.DrawPolyline(points, Paint.Stroke(Color.White, 1f), close: true);
        AssertLastPathIs(context, new PathBuilder().AddPolyline(points, close: true));

        context.DrawArc(center, radii, Angle.Deg(15d), Angle.Deg(200d), ArcMode.Pie, Paint.Fill(Color.White));
        AssertLastPathIs(
            context,
            new PathBuilder().AddArc(center, radii, Angle.Deg(15d), Angle.Deg(200d), ArcMode.Pie));
    }

    [TestMethod]
    public void ScalarSugarForwardsToTheVirtualConvenience()
    {
        var context = new RecordingContext();
        var center = new Vector2(3f, 3f);
        var bounds = new Rect(0f, 0f, 8f, 8f);

        context.DrawEllipse(center, 4f, 2f, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddEllipse(center, new Vector2(4f, 2f)));

        context.DrawRoundRect(bounds, 2f, Paint.Fill(Color.White));
        AssertLastPathIs(context, new PathBuilder().AddRoundRect(bounds, new Vector2(2f, 2f)));

        context.DrawArc(center, 4f, Angle.Zero, Angle.QuarterTurn, ArcMode.Chord, Paint.Fill(Color.White));
        AssertLastPathIs(
            context,
            new PathBuilder().AddArc(center, new Vector2(4f, 4f), Angle.Zero, Angle.QuarterTurn, ArcMode.Chord));

        Assert.ThrowsExactly<ArgumentNullException>(
            () => DrawContext2DExtensions.DrawRoundRect(null!, default, 1f, default));
    }

    [TestMethod]
    public void ConveniencesShareOneScratchBuilderAndLeaveItEmpty()
    {
        var context = new RecordingContext();
        var separate = new RecordingContext();

        context.DrawCircle(Vector2.Zero, 1f, Paint.Fill(Color.White));
        var first = context.LastBuilder;
        context.DrawRect(new Rect(0f, 0f, 1f, 1f), Paint.Fill(Color.White));

        Assert.AreSame(
            first,
            context.LastBuilder,
            "Every convenience must build into the one context-owned scratch builder.");
        Assert.AreEqual(
            0,
            context.LastBuilder!.Count,
            "The lease must empty the builder on release, so nothing leaks into the next call.");
        separate.DrawCircle(Vector2.Zero, 1f, Paint.Fill(Color.White));
        Assert.AreNotSame(
            first,
            separate.LastBuilder,
            "The scratch is per context, not shared between contexts.");
    }

    [TestMethod]
    public void AnAuthorsHalfBuiltPathIsUntouchedByInterleavedConveniences()
    {
        // This is the re-entrancy question that actually matters to an author: they are assembling
        // geometry across several statements and a convenience runs in between. The scratch is
        // private to the context, so there is nothing shared for it to disturb.
        var context = new RecordingContext();
        var authored = new PathBuilder().MoveTo(0f, 0f).LineTo(10f, 0f);

        context.DrawCircle(new Vector2(5f, 5f), 3f, Paint.Fill(Color.White));
        authored.LineTo(10f, 10f);
        context.DrawRoundRect(new Rect(0f, 0f, 4f, 4f), new Vector2(1f, 1f), Paint.Fill(Color.White));
        authored.Close();

        var commands = authored.Commands;
        Assert.AreEqual(4, commands.Length, "The author's path must hold exactly what the author appended.");
        Assert.AreEqual(new Vector2(0f, 0f), commands[0].Point1);
        Assert.AreEqual(new Vector2(10f, 0f), commands[1].Point1);
        Assert.AreEqual(new Vector2(10f, 10f), commands[2].Point1);
        Assert.AreEqual(PathVerb.Close, commands[3].Verb);
        Assert.AreNotSame(authored, context.LastBuilder, "An author's builder is never the scratch.");
    }

    [TestMethod]
    public void ReEnteringTheScratchFailsLoudlyRatherThanCorruptingTheOuterShape()
    {
        var context = new ReentrantContext();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.DrawCircle(Vector2.Zero, 4f, Paint.Fill(Color.White)));

        Assert.Contains(
            "scratch path is already rented",
            error.Message,
            "The failure must name the scratch, so the bug is diagnosable where it happens.");
        Assert.AreEqual(1, context.OuterDrawPathCount, "The outer call reached DrawPath before re-entering.");
    }

    [TestMethod]
    public void AThrowingDrawPathReleasesTheScratchForTheNextCall()
    {
        var context = new ThrowingContext();

        Assert.ThrowsExactly<NotSupportedException>(
            () => context.DrawCircle(Vector2.Zero, 1f, Paint.Fill(Color.White)));

        context.Fail = false;
        context.DrawCircle(Vector2.Zero, 1f, Paint.Fill(Color.White));
        Assert.AreEqual(2, context.DrawPathCount, "A failed draw must leave the scratch rentable, not poisoned.");
    }

    [TestMethod]
    public void ABackendMayStillOverrideAConvenience()
    {
        // §7.2 keeps the override door open even though the Skia backend does not walk through it.
        var context = new OverridingContext();

        context.DrawCircle(Vector2.Zero, 4f, Paint.Fill(Color.White));

        Assert.AreEqual(1, context.CircleOverrideCount);
        Assert.AreEqual(0, context.DrawPathCount, "The override replaced the portable lowering entirely.");
    }

    [TestMethod]
    public void DegeneratePolylineStillIssuesOneDrawPathWithNoGeometry()
    {
        var context = new RecordingContext();

        context.DrawPolyline([new Vector2(1f, 1f)], Paint.Stroke(Color.White, 1f));

        Assert.AreEqual(1, context.DrawPathCount, "One convenience call is always one Tier-1 call.");
        Assert.IsEmpty(context.LastCommands, "A lone vertex describes no contour.");
    }

    [TestMethod]
    public void WarmConvenienceLoopAllocatesNoManagedBytes()
    {
        var context = new CountingContext();
        var center = new Vector2(3f, 4f);
        var radii = new Vector2(5f, 2f);
        var bounds = new Rect(0f, 0f, 8f, 6f);
        var corners = new Vector2(2f, 1f);
        var points = new[] { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 3f) };
        var fill = Paint.Fill(Color.White);
        var stroke = Paint.Stroke(Color.White, 2f);

        var reading = AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                context.DrawCircle(center, 4f, fill);
                context.DrawEllipse(center, radii, fill);
                context.DrawRect(bounds, fill);
                context.DrawRoundRect(bounds, corners, fill);
                context.DrawLine(center, radii, stroke);
                context.DrawPolyline(points, stroke, close: true);
                context.DrawArc(center, radii, Angle.Zero, Angle.Deg(200d), ArcMode.Pie, fill);
            },
            "The warm Tier-2 convenience loop");

        Assert.AreEqual(
            reading.Invocations * 7,
            context.DrawPathCount,
            "Every probe invocation must have issued all seven Tier-1 calls.");
    }

    private static void DrawEveryConvenience(IDrawContext2D context)
    {
        var paint = Paint.Fill(Color.White);
        context.DrawCircle(Vector2.Zero, 1f, paint);
        context.DrawEllipse(Vector2.Zero, new Vector2(2f, 1f), paint);
        context.DrawRect(new Rect(0f, 0f, 2f, 2f), paint);
        context.DrawRoundRect(new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), paint);
        context.DrawLine(Vector2.Zero, Vector2.One, Paint.Stroke(Color.White, 1f));
        context.DrawPolyline([Vector2.Zero, Vector2.One, new Vector2(2f, 0f)], Paint.Stroke(Color.White, 1f));
        context.DrawArc(Vector2.Zero, Vector2.One, Angle.Zero, Angle.QuarterTurn, ArcMode.Open, paint);
    }

    private static void AssertLastPathIs(RecordingContext context, PathBuilder expected)
    {
        var actual = context.LastCommands;
        var commands = expected.Commands;
        Assert.HasCount(commands.Length, actual, "Command counts must match.");
        for (var index = 0; index < commands.Length; index++)
        {
            Assert.AreEqual(commands[index].Verb, actual[index].Verb, $"Verb {index}.");
            Assert.AreEqual(commands[index].Point1, actual[index].Point1, $"Command {index} point 1.");
            Assert.AreEqual(commands[index].Point2, actual[index].Point2, $"Command {index} point 2.");
            Assert.AreEqual(commands[index].Point3, actual[index].Point3, $"Command {index} point 3.");
        }
    }

    /// <summary>A second <see cref="IDrawContext2D"/> implementation, inheriting Tier 2 for free.</summary>
    private class CountingContext : DrawContext2DBase
    {
        internal int DrawPathCount { get; private set; }

        internal int DrawTextCount { get; private set; }

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
            OnPath(path);
        }

        // Tier 1 with no portable default, so no convenience routes through it and these tests
        // never call it; counted anyway, because a convenience that started drawing text instead
        // of a path would otherwise register as drawing nothing at all.
        public override void DrawText(ITextLayout layout, Color? colorOverride = null) => DrawTextCount++;

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

        /// <summary>Hook for subclasses that need the geometry; allocation-free when unused.</summary>
        protected virtual void OnPath(PathBuilder path)
        {
        }
    }

    private sealed class RecordingContext : CountingContext
    {
        private readonly List<PathCommand> lastCommands = [];

        internal PathBuilder? LastBuilder { get; private set; }

        internal IReadOnlyList<PathCommand> LastCommands => lastCommands;

        protected override void OnPath(PathBuilder path)
        {
            LastBuilder = path;
            lastCommands.Clear();
            foreach (var command in path.Commands)
            {
                lastCommands.Add(command);
            }
        }
    }

    /// <summary>A backend whose Tier-1 lowering wrongly calls back into a Tier-2 convenience.</summary>
    private sealed class ReentrantContext : CountingContext
    {
        internal int OuterDrawPathCount { get; private set; }

        protected override void OnPath(PathBuilder path)
        {
            OuterDrawPathCount++;
            DrawRect(new Rect(0f, 0f, 1f, 1f), Paint.Fill(Color.White));
        }
    }

    private sealed class ThrowingContext : CountingContext
    {
        internal bool Fail { get; set; } = true;

        protected override void OnPath(PathBuilder path)
        {
            if (Fail)
            {
                throw new NotSupportedException("Simulated backend failure.");
            }
        }
    }

    private sealed class OverridingContext : CountingContext
    {
        internal int CircleOverrideCount { get; private set; }

        public override void DrawCircle(in Vector2 center, float radius, in Paint paint) =>
            CircleOverrideCount++;
    }
}
