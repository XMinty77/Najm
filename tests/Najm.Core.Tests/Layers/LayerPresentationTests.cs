using System.Numerics;
using Najm.Utils;
using Najm.Core.Text;

namespace Najm.Core.Tests.Layers;

[TestClass]
public sealed class LayerPresentationTests
{
    [TestMethod]
    public void PresentationStateStartsAtTheDocumentedDefaults()
    {
        var layer = new ProbeLayer();

        Assert.IsTrue(layer.Visible);
        Assert.AreEqual(1f, layer.Opacity);
        Assert.AreEqual(BlendMode.SrcOver, layer.Blend);
        Assert.AreEqual(Color.Transparent, layer.ClearColor);
        Assert.IsNull(layer.Viewport);
        Assert.IsFalse(layer.YAxisPointsUp);
    }

    [TestMethod]
    public void PresentationStateRoundTripsAssignedValues()
    {
        var viewport = new Rect(10f, 20f, 640f, 360f);
        var layer = new ProbeLayer
        {
            Visible = false,
            Opacity = 0.25f,
            Blend = BlendMode.Multiply,
            ClearColor = Color.Black,
            Viewport = viewport,
        };

        Assert.IsFalse(layer.Visible);
        Assert.AreEqual(0.25f, layer.Opacity);
        Assert.AreEqual(BlendMode.Multiply, layer.Blend);
        Assert.AreEqual(Color.Black, layer.ClearColor);
        Assert.AreEqual(viewport, layer.Viewport);

        layer.Opacity = 0f;
        Assert.AreEqual(0f, layer.Opacity);
        layer.Opacity = 1f;
        Assert.AreEqual(1f, layer.Opacity);
        layer.Viewport = null;
        Assert.IsNull(layer.Viewport);
    }

    [TestMethod]
    public void InvalidPresentationStateIsRejected()
    {
        var layer = new ProbeLayer();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Opacity = -0.001f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Opacity = 1.001f);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Opacity = float.NaN);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Opacity = float.PositiveInfinity);
        Assert.ThrowsExactly<ArgumentException>(() => layer.Blend = (BlendMode)9999);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Viewport = new Rect(0f, 0f, 0f, 10f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.Viewport = default(Rect));

        Assert.AreEqual(1f, layer.Opacity);
        Assert.AreEqual(BlendMode.SrcOver, layer.Blend);
        Assert.IsNull(layer.Viewport);
    }

    [TestMethod]
    public void RenderBracketHooksFireWithTheSuppliedContext()
    {
        var layer = new ProbeLayer();
        var context = new ProbeDrawContext2D();

        Assert.AreEqual(0, layer.BeforeRenderCount);
        Assert.AreEqual(0, layer.AfterRenderCount);

        layer.InvokeBeforeRender(context);
        layer.InvokeAfterRender(context);

        Assert.AreEqual(1, layer.BeforeRenderCount);
        Assert.AreEqual(1, layer.AfterRenderCount);
        Assert.AreSame(context, layer.LastBeforeContext);
        Assert.AreSame(context, layer.LastAfterContext);
    }

    [TestMethod]
    public void RenderBracketHooksAreOptional()
    {
        var layer = new ScreenLayer();
        var context = new ProbeDrawContext2D();

        layer.InvokeBeforeRender(context);
        layer.InvokeAfterRender(context);

        Assert.AreEqual(0, context.DrawCount);
    }

    private sealed class ProbeLayer : Layer
    {
        private readonly Node2D root = new();

        internal int BeforeRenderCount { get; private set; }

        internal int AfterRenderCount { get; private set; }

        internal IDrawContext2D? LastBeforeContext { get; private set; }

        internal IDrawContext2D? LastAfterContext { get; private set; }

        protected override Node RootNode => root;

        protected override void OnBeforeRender(IDrawContext2D context)
        {
            BeforeRenderCount++;
            LastBeforeContext = context;
        }

        protected override void OnAfterRender(IDrawContext2D context)
        {
            AfterRenderCount++;
            LastAfterContext = context;
        }
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

        public override void DrawText(ITextLayout layout, Color? colorOverride = null) => DrawCount++;

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
}
