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
    public void BoundsDefaultToEmptyAndChainThroughGeometry()
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

    private sealed class ProbeDrawContext2D : IDrawContext2D
    {
        public SurfaceSpec SurfaceSpec { get; } = new(64, 64);

        public RenderCaps Caps => RenderCaps.None;

        public float RenderScale => 1f;

        public float Scale => 1f;

        internal int DrawCount { get; private set; }

        public void Clear(Color color) => DrawCount++;

        public void DrawPath(PathBuilder path, in Paint paint) => DrawCount++;

        public void DrawImage(
            IImage image,
            in Matrix3x2 imageToLocal,
            ImageSampling sampling = ImageSampling.Linear) => DrawCount++;

        public void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
        }

        public void BeginLayerBracket(in LayerBracket bracket)
        {
        }

        public void EndLayerBracket()
        {
        }

        public void BeginUnitBracket(in UnitBracket bracket)
        {
        }

        public void EndUnitBracket()
        {
        }

        public void PushTransform(in Matrix3x2 localTransform)
        {
        }

        public void PopTransform()
        {
        }

        public void PushClip(in Rect bounds)
        {
        }

        public void PushClip(PathBuilder path)
        {
        }

        public void PopClip()
        {
        }

        public void PushOpacity(float opacity)
        {
        }

        public void PopOpacity()
        {
        }
    }

    private static class RenderTicks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
