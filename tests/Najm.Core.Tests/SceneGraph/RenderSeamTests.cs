using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.SceneGraph;

[TestClass]
public sealed class RenderSeamTests
{
    [TestMethod]
    public void DefaultRenderDrawsNothingAndDoesNotThrow()
    {
        var node = new Node2D();
        var context = new ProbeDrawContext2D();

        node.InvokeRender(context);

        Assert.AreEqual(0, context.DrawCount);
    }

    [TestMethod]
    public void DrawableRenderRunsThroughInvokeRender()
    {
        var drawable = new ProbeDrawable();
        var context = new ProbeDrawContext2D();

        drawable.InvokeRender(context);
        drawable.InvokeRender(context);

        Assert.AreEqual(2, drawable.RenderCount);
        Assert.AreEqual(2, context.DrawCount);
        Assert.AreSame(context, drawable.LastContext);
    }

    [TestMethod]
    public void PaintOrderKeepsInsertionOrderForEqualZIndex()
    {
        var parent = new Node2D();
        var first = parent.Add(new Node2D { ZIndex = 3 });
        var second = parent.Add(new Node2D { ZIndex = 3 });
        var third = parent.Add(new Node2D { ZIndex = 3 });

        Assert.AreSame(first, parent.GetChildInPaintOrder(0));
        Assert.AreSame(second, parent.GetChildInPaintOrder(1));
        Assert.AreSame(third, parent.GetChildInPaintOrder(2));
    }

    [TestMethod]
    public void PaintOrderSortsAscendingAndBreaksTiesByInsertion()
    {
        var parent = new Node2D();
        var backA = parent.Add(new Node2D { ZIndex = -1 });
        var middleA = parent.Add(new Node2D());
        var frontA = parent.Add(new Node2D { ZIndex = 5 });
        var middleB = parent.Add(new Node2D());
        var backB = parent.Add(new Node2D { ZIndex = -1 });
        var frontB = parent.Add(new Node2D { ZIndex = 5 });

        Assert.AreSame(backA, parent.GetChildInPaintOrder(0));
        Assert.AreSame(backB, parent.GetChildInPaintOrder(1));
        Assert.AreSame(middleA, parent.GetChildInPaintOrder(2));
        Assert.AreSame(middleB, parent.GetChildInPaintOrder(3));
        Assert.AreSame(frontA, parent.GetChildInPaintOrder(4));
        Assert.AreSame(frontB, parent.GetChildInPaintOrder(5));
    }

    [TestMethod]
    public void PaintOrderTracksZIndexChangesAndTopologyChanges()
    {
        var parent = new Node2D();
        var first = parent.Add(new Node2D());
        var second = parent.Add(new Node2D());

        Assert.AreSame(first, parent.GetChildInPaintOrder(0));

        first.ZIndex = 10;

        Assert.AreSame(second, parent.GetChildInPaintOrder(0));
        Assert.AreSame(first, parent.GetChildInPaintOrder(1));

        var third = parent.Add(new Node2D { ZIndex = -4 });

        Assert.AreSame(third, parent.GetChildInPaintOrder(0));
        Assert.AreSame(second, parent.GetChildInPaintOrder(1));
        Assert.AreSame(first, parent.GetChildInPaintOrder(2));

        Assert.IsTrue(parent.Remove(first));

        Assert.AreSame(third, parent.GetChildInPaintOrder(0));
        Assert.AreSame(second, parent.GetChildInPaintOrder(1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => parent.GetChildInPaintOrder(2));
    }

    [TestMethod]
    public void ZIndexReordersPaintOrderWithoutReorderingUpdate()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var updates = new List<string>();
        var first = layer.Root.Add(new RecordingNode("first", updates) { ZIndex = 7 });
        var second = layer.Root.Add(new RecordingNode("second", updates));
        scene.Load(TestEnvironment.Stub());

        scene.Tick(RenderTicks.At(0));

        Assert.AreSame(second, layer.Root.GetChildInPaintOrder(0));
        Assert.AreSame(first, layer.Root.GetChildInPaintOrder(1));
        Assert.AreEqual("first,second", string.Join(',', updates));
    }

    [TestMethod]
    public void PerNodeBoundsDefaultToEmptyAndChainThroughGeometry()
    {
        var plain = new Node2D();

        Assert.AreEqual(default, plain.GeometryBounds);
        Assert.AreEqual(default, plain.HitBounds);
        Assert.AreEqual(default, plain.VisualBounds);
        Assert.IsTrue(plain.GeometryBounds.IsEmpty);

        var measured = new MeasuredDrawable(new Rect(-2f, -3f, 4f, 6f));

        Assert.AreEqual(new Rect(-2f, -3f, 4f, 6f), measured.GeometryBounds);
        Assert.AreEqual(measured.GeometryBounds, measured.HitBounds);
        Assert.AreEqual(measured.GeometryBounds, measured.VisualBounds);

        var expanded = new ExpandedDrawable(new Rect(0f, 0f, 10f, 10f));

        Assert.AreEqual(new Rect(0f, 0f, 10f, 10f), expanded.GeometryBounds);
        Assert.AreEqual(expanded.GeometryBounds, expanded.HitBounds);
        Assert.AreEqual(new Rect(-1f, -1f, 12f, 12f), expanded.VisualBounds);

        // A childless node's aggregates are its own declarations: the split below only shows up
        // once something hangs beneath it.
        Assert.AreEqual(expanded.GeometryBounds, expanded.SubtreeGeometryBounds);
        Assert.AreEqual(expanded.HitBounds, expanded.SubtreeHitBounds);
        Assert.AreEqual(expanded.VisualBounds, expanded.SubtreeVisualBounds);
    }

    [TestMethod]
    public void TheSubtreeAggregateCoversDescendantsWhereThePerNodeDeclarationDoesNot()
    {
        // §6.6 calls visual bounds "the conservative visible output of the node and descendants"
        // and gives each of the three a subtree aggregate. The declaration is the node's own —
        // an override cannot speak for children it has never seen — and the aggregate is the
        // node-and-descendants value §6.7 must size an isolation bracket from. Sizing a bracket
        // from the declaration would clip everything the child paints outside the parent.
        var parent = new ExpandedDrawable(new Rect(0f, 0f, 10f, 10f));
        parent.Add(new MeasuredDrawable(new Rect(0f, 0f, 4f, 4f)) { Position = new Vector2(30f, 0f) });

        Assert.AreEqual(new Rect(-1f, -1f, 12f, 12f), parent.VisualBounds);
        Assert.AreEqual(new Rect(-1f, -1f, 35f, 12f), parent.SubtreeVisualBounds);
        Assert.AreEqual(new Rect(0f, 0f, 34f, 10f), parent.SubtreeGeometryBounds);
        Assert.AreEqual(new Rect(0f, 0f, 34f, 10f), parent.SubtreeHitBounds);
    }

    private sealed class RecordingNode(string name, List<string> updates) : Node2D
    {
        protected override void Update(in TickContext tick) => updates.Add(name);
    }

    private sealed class ProbeDrawable : Drawable
    {
        internal int RenderCount { get; private set; }

        internal IDrawContext2D? LastContext { get; private set; }

        public override void Render(IDrawContext2D context)
        {
            RenderCount++;
            LastContext = context;
            context.DrawPath(new PathBuilder(), default);
        }
    }

    private class MeasuredDrawable(Rect geometry) : Drawable
    {
        public override Rect GeometryBounds { get; } = geometry;

        public override void Render(IDrawContext2D context)
        {
        }
    }

    private sealed class ExpandedDrawable(Rect geometry) : MeasuredDrawable(geometry)
    {
        public override Rect VisualBounds =>
            new(
                GeometryBounds.X - 1f,
                GeometryBounds.Y - 1f,
                GeometryBounds.Width + 2f,
                GeometryBounds.Height + 2f);
    }

    private sealed class ProbeDrawContext2D : DrawContext2DBase
    {
        public override SurfaceSpec SurfaceSpec { get; } = new(64, 64);

        public override RenderCaps Caps => RenderCaps.None;

        public override float RenderScale => 1f;

        public override float Scale => 1f;

        internal int DrawCount { get; private set; }

        public override void Clear(Color color) => DrawCount++;

        public override void DrawPath(PathBuilder path, in Paint paint) => DrawCount++;

        public override void DrawImage(
            IImage image,
            in Matrix3x2 imageToLocal,
            ImageSampling sampling = ImageSampling.Linear) => DrawCount++;

        public override void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
        }

        public override void BeginLayerBracket(in LayerBracket bracket)
        {
        }

        public override void EndLayerBracket()
        {
        }

        public override void BeginUnitBracket(in UnitBracket bracket)
        {
        }

        public override void EndUnitBracket()
        {
        }

        public override void BeginClipBracket(in ClipBracket bracket)
        {
        }

        public override void EndClipBracket()
        {
        }

        public override void PushTransform(in Matrix3x2 localTransform)
        {
        }

        public override void PopTransform()
        {
        }

        public override void PushClip(in Rect bounds)
        {
        }

        public override void PushClip(PathBuilder path)
        {
        }

        public override void PopClip()
        {
        }

        public override void PushOpacity(float opacity)
        {
        }

        public override void PopOpacity()
        {
        }
    }

    private static class RenderTicks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
