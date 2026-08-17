using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class RenderTraverserTests
{
    [TestMethod]
    public void VirtualResolutionDefaultsToFullHdAndRejectsNonPositiveSizes()
    {
        Assert.AreEqual(new Vector2(1920f, 1080f), new Scene().VirtualResolution);
        Assert.AreEqual(
            new Vector2(640f, 480f),
            new Scene { VirtualResolution = new Vector2(640f, 480f) }.VirtualResolution);

        foreach (var invalid in new[]
                 {
                     new Vector2(0f, 1080f),
                     new Vector2(1920f, 0f),
                     new Vector2(-1920f, 1080f),
                     new Vector2(float.NaN, 1080f),
                     new Vector2(1920f, float.PositiveInfinity),
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => _ = new Scene { VirtualResolution = invalid });
        }
    }

    [TestMethod]
    public void ScreenLayerBaseIsTheRenderScaleAndWorldLayerBaseCarriesTheCameraFlip()
    {
        var resolution = new Vector2(8f, 4f);
        var screenBase = RenderTraverser.ComputeLayerBase(new ScreenLayer(), resolution, 2f);

        Assert.AreEqual(Matrix3x2.CreateScale(2f), screenBase);
        Assert.AreEqual(new Vector2(6f, 2f), Vector2.Transform(new Vector2(3f, 1f), screenBase));

        var world = new WorldLayer2D();
        var worldBase = RenderTraverser.ComputeLayerBase(world, resolution, 2f);

        // World origin sits on the virtual centre (4, 2), which is device (8, 4) at twice scale;
        // one world unit of +Y lands one virtual unit toward the top, i.e. device (8, 2).
        AssertPoint(new Vector2(8f, 4f), Vector2.Transform(Vector2.Zero, worldBase));
        AssertPoint(new Vector2(8f, 2f), Vector2.Transform(Vector2.UnitY, worldBase));
        AssertPoint(new Vector2(10f, 4f), Vector2.Transform(Vector2.UnitX, worldBase));
    }

    [TestMethod]
    public void EngineTransformAppliesTheNodeWorldMatrixBeforeTheLayerBase()
    {
        var scene = new Scene { VirtualResolution = new Vector2(100f, 100f) };
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(10f, 0f);
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 3f);
        scene.RenderDirect(context);

        // world × base: local (0,0) translates to (10,0) and then scales to (30,0).
        // The rejected base × world order would scale first and land on (10,0).
        var engine = log.Single("node");
        AssertPoint(new Vector2(30f, 0f), Vector2.Transform(Vector2.Zero, engine));
        AssertPoint(new Vector2(33f, 3f), Vector2.Transform(Vector2.One, engine));
        Assert.AreEqual(node.WorldMatrix * Matrix3x2.CreateScale(3f), engine);
    }

    [TestMethod]
    public void NestedNodesComposeParentThenLayerBase()
    {
        var scene = new Scene { VirtualResolution = new Vector2(100f, 100f) };
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var parent = layer.Root.Add(new LoggingDrawable("parent", log));
        parent.Position = new Vector2(4f, 0f);
        parent.Scale = new Vector2(2f, 2f);
        var child = parent.Add(new LoggingDrawable("child", log));
        child.Position = new Vector2(1f, 0f);
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(context);

        // Child local (0,0) → parent local (1,0) → layer (4,0)+(2,0) = (6,0) → device (12,0).
        AssertPoint(new Vector2(12f, 0f), Vector2.Transform(Vector2.Zero, log.Single("child")));
        AssertPoint(new Vector2(8f, 0f), Vector2.Transform(Vector2.Zero, log.Single("parent")));
    }

    [TestMethod]
    public void TraversalIsDepthFirstPreOrderInPaintOrder()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var front = layer.Root.Add(new LoggingDrawable("front", log) { ZIndex = 5 });
        front.Add(new LoggingDrawable("front.child", log));
        var back = layer.Root.Add(new LoggingDrawable("back", log) { ZIndex = -1 });
        back.Add(new LoggingDrawable("back.child", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("back,back.child,front,front.child", log.Names);
    }

    [TestMethod]
    public void LayerHooksBracketTheWalkAndSeeTheLayerBaseTransform()
    {
        var scene = new Scene { VirtualResolution = new Vector2(64f, 32f) };
        var log = new RenderLog();
        var layer = scene.Layers.Add(new HookLayer("layer", log));
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(5f, 5f);
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext(renderScale: 2f));

        Assert.AreEqual("layer.before,node,layer.after", log.Names);
        Assert.AreEqual(Matrix3x2.CreateScale(2f), log.Single("layer.before"));
        Assert.AreEqual(
            Matrix3x2.CreateScale(2f),
            log.Single("layer.after"),
            "OnAfterRender must see layer space again, not the last node's transform.");
    }

    [TestMethod]
    public void LayersRenderInAddOrderAndSkipWholeWhenTheyCannotContribute()
    {
        var scene = new Scene();
        var log = new RenderLog();
        var hidden = scene.Layers.Add(new HookLayer("hidden", log));
        hidden.Root.Add(new LoggingDrawable("hidden.node", log));
        hidden.Visible = false;
        var transparent = scene.Layers.Add(new HookLayer("transparent", log));
        transparent.Root.Add(new LoggingDrawable("transparent.node", log));
        transparent.Opacity = 0f;
        var shown = scene.Layers.Add(new HookLayer("shown", log));
        shown.Root.Add(new LoggingDrawable("shown.node", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("shown.before,shown.node,shown.after", log.Names);
        Assert.IsFalse(RenderTraverser.ParticipatesInRender(hidden));
        Assert.IsFalse(RenderTraverser.ParticipatesInRender(transparent));
        Assert.IsTrue(RenderTraverser.ParticipatesInRender(shown));

        log.Clear();
        hidden.Visible = true;
        transparent.Opacity = 1f;
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(
            "hidden.before,hidden.node,hidden.after," +
            "transparent.before,transparent.node,transparent.after," +
            "shown.before,shown.node,shown.after",
            log.Names);
    }

    [TestMethod]
    public void VisibleFalseSkipsTheSubtreeAndEnabledFalseOnlySkipsUpdate()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var invisible = layer.Root.Add(new LoggingDrawable("invisible", log) { Visible = false });
        var invisibleChild = invisible.Add(new LoggingDrawable("invisible.child", log));
        var disabled = layer.Root.Add(new LoggingDrawable("disabled", log) { Enabled = false });
        var disabledChild = disabled.Add(new LoggingDrawable("disabled.child", log));
        scene.Load(TestEnvironment.Stub());

        scene.Tick(Ticks.At(0));
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(
            "disabled,disabled.child",
            log.Names,
            "Visible gates rendering for the whole subtree; Enabled must not.");
        Assert.AreEqual(1, invisible.UpdateCount, "An invisible node must still update.");
        Assert.AreEqual(1, invisibleChild.UpdateCount, "An invisible subtree must still update.");
        Assert.AreEqual(0, disabled.UpdateCount, "A disabled node must not update.");
        Assert.AreEqual(0, disabledChild.UpdateCount, "A disabled subtree must not update.");
    }

    [TestMethod]
    public void RenderDirectFoldsTheContextRenderScaleIntoEveryEngineTransform()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        layer.Root.Add(new LoggingDrawable("node", log)).Position = new Vector2(3f, 7f);
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext(renderScale: 1.5f));

        AssertPoint(new Vector2(4.5f, 10.5f), Vector2.Transform(Vector2.Zero, log.Single("node")));
    }

    [TestMethod]
    public void RenderIsLegalBeforeTheFirstTickAndLeavesTheSceneReadyToStart()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("node", log.Names);
        Assert.AreEqual(0, node.UpdateCount, "A node may render before it has ever updated.");
        Assert.AreEqual(SceneState.Loaded, scene.State);

        scene.Tick(Ticks.At(0));

        Assert.AreEqual(1, node.UpdateCount);
        Assert.AreEqual(SceneState.Started, scene.State);
    }

    [TestMethod]
    public void RenderRequiresALoadedSceneAndRejectsReentrancy()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var context = new RecordingContext();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.RenderDirect(context));

        scene.Load(TestEnvironment.Stub());
        var reentrant = layer.Root.Add(new ReentrantDrawable(scene, context));
        scene.RenderDirect(context);

        Assert.IsInstanceOfType<InvalidOperationException>(reentrant.Failure);
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.RenderDirect(null!));

        scene.Stop();
        scene.Unload();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.RenderDirect(context));
    }

    [TestMethod]
    public void RenderDirectDoesNotMutateObservableSceneState()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(2f, 3f);
        scene.Load(TestEnvironment.Stub());
        scene.Tick(Ticks.At(0));

        var world = node.WorldMatrix;
        var position = node.Position;
        var updateCount = node.UpdateCount;

        scene.RenderDirect(new RecordingContext());
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(world, node.WorldMatrix);
        Assert.AreEqual(position, node.Position);
        Assert.AreEqual(updateCount, node.UpdateCount);
        Assert.AreEqual(SceneState.Started, scene.State);
        Assert.AreEqual(1, scene.Layers.Count);
        Assert.AreSame(layer, node.Layer);

        scene.Tick(Ticks.At(1));

        Assert.AreEqual(2, node.UpdateCount, "Rendering must not disturb the tick sequence.");
    }

    [TestMethod]
    public void WarmRenderTraversalAllocatesNoManagedMemory()
    {
        var scene = new Scene();
        var screen = scene.Layers.Add(new ScreenLayer());
        var world = scene.Layers.Add(new WorldLayer2D());
        var parent = screen.Root.Add(new SilentDrawable());
        parent.Add(new SilentDrawable { ZIndex = 3 });
        parent.Add(new SilentDrawable { ZIndex = -3 });
        world.Root.Add(new SilentDrawable()).Position = new Vector2(1f, 2f);
        scene.Load(TestEnvironment.Stub());
        scene.Tick(Ticks.At(0));

        var context = new SilentContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        const int measuredRenders = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var render = 0; render < measuredRenders; render++)
        {
            scene.RenderDirect(context);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, allocated, $"The warm render traversal allocated {allocated} managed bytes.");
        Assert.AreEqual((64 + measuredRenders) * 4, context.DrawCount);
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-4f, $"Expected {expected} but mapped to {actual}.");
        Assert.AreEqual(expected.Y, actual.Y, 1e-4f, $"Expected {expected} but mapped to {actual}.");
    }

    private sealed class RenderLog
    {
        private readonly List<(string Name, Matrix3x2 Engine)> entries = [];

        internal string Names => string.Join(',', entries.Select(entry => entry.Name));

        internal void Add(string name, in Matrix3x2 engine) => entries.Add((name, engine));

        internal void Clear() => entries.Clear();

        internal Matrix3x2 Single(string name) =>
            entries.Single(entry => entry.Name == name).Engine;
    }

    private sealed class LoggingDrawable(string name, RenderLog log) : Drawable
    {
        internal int UpdateCount { get; private set; }

        public override void Render(IDrawContext2D context) =>
            log.Add(name, ((RecordingContext)context).Engine);

        protected override void Update(in TickContext tick) => UpdateCount++;
    }

    private sealed class ReentrantDrawable(Scene scene, IDrawContext2D context) : Drawable
    {
        internal Exception? Failure { get; private set; }

        public override void Render(IDrawContext2D drawContext)
        {
            try
            {
                scene.RenderDirect(context);
            }
            catch (Exception exception)
            {
                Failure = exception;
            }
        }
    }

    private sealed class SilentDrawable : Drawable
    {
        public override void Render(IDrawContext2D context) => context.DrawPath(EmptyPath, default);

        private static readonly PathBuilder EmptyPath = new();
    }

    private sealed class HookLayer(string name, RenderLog log) : ScreenLayer
    {
        protected override void OnBeforeRender(IDrawContext2D context) =>
            log.Add($"{name}.before", ((RecordingContext)context).Engine);

        protected override void OnAfterRender(IDrawContext2D context) =>
            log.Add($"{name}.after", ((RecordingContext)context).Engine);
    }

    private class SilentContext : IDrawContext2D
    {
        internal SilentContext(float renderScale = 1f) => RenderScale = renderScale;

        public SurfaceSpec SurfaceSpec { get; } = new(64, 64);

        public RenderCaps Caps => RenderCaps.None;

        public float RenderScale { get; }

        public float Scale => 1f;

        internal int DrawCount { get; private set; }

        public void Clear(Color color)
        {
        }

        public void DrawPath(PathBuilder path, in Paint paint) => DrawCount++;

        public void DrawImage(IImage image, in Matrix3x2 imageToLocal, ImageSampling sampling = ImageSampling.Linear)
        {
        }

        public virtual void SetEngineTransform(in Matrix3x2 engineToDevice)
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

    private sealed class RecordingContext(float renderScale = 1f) : SilentContext(renderScale)
    {
        internal Matrix3x2 Engine { get; private set; }

        public override void SetEngineTransform(in Matrix3x2 engineToDevice) => Engine = engineToDevice;
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
