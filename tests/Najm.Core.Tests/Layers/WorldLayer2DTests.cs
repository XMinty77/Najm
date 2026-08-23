using System.Numerics;

namespace Najm.Core.Tests.Layers;

[TestClass]
public sealed class WorldLayer2DTests
{
    [TestMethod]
    public void ADefaultLayerIsImmediatelyUsable()
    {
        var layer = new WorldLayer2D();

        Assert.IsNotNull(layer.Root);
        Assert.IsNotNull(layer.Camera);
        Assert.AreSame(layer.Root, layer.Camera.Parent);
        Assert.AreEqual(Vector2.Zero, layer.Camera.Position);
        Assert.AreEqual(1f, layer.Camera.Zoom);

        // The reference's own usage: frame content right after adding the layer.
        layer.Camera.FitRect(new Rect(-2f, -1f, 4f, 2f), new Vector2(1920f, 1080f));
        Assert.AreEqual(480f, layer.Camera.Zoom, 1e-4f);
    }

    [TestMethod]
    public void TheOneArgumentFitRectFramesTheScenesVirtualResolution()
    {
        // ARCHITECTURE Appendix B.2 writes layer.Camera.FitRect(rect) with one argument. The
        // convenience lives on the layer because the layer is what can reach the extent; the fit
        // itself is still Camera2D's.
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = scene.Layers.Add(new WorldLayer2D());

        layer.FitRect(new Rect(-10f, -10f, 20f, 20f));

        // min(800/20, 600/20) = 30, centred on the rectangle's own centre.
        Assert.AreEqual(30f, layer.Camera.Zoom, 1e-4f);
        Assert.AreEqual(Vector2.Zero, layer.Camera.Position);

        // It reads the scene rather than a constant: a different resolution fits differently.
        var wider = new Scene { VirtualResolution = new Vector2(1920f, 1080f) };
        var widerLayer = wider.Layers.Add(new WorldLayer2D());

        widerLayer.FitRect(new Rect(-10f, -10f, 20f, 20f));

        Assert.AreEqual(54f, widerLayer.Camera.Zoom, 1e-4f);
    }

    [TestMethod]
    public void TheOneArgumentFitRectFramesAViewportRatherThanTheFrame()
    {
        // DEVIATIONS entry 7's warning, and RenderTraverser.ComputeLayerBase's rule: the extent a
        // viewport'd layer frames is its viewport. Fitting against the scene instead would frame a
        // rectangle the render then crops to a quarter of its width.
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = scene.Layers.Add(new WorldLayer2D
        {
            Viewport = new Rect(100f, 50f, 200f, 150f),
        });

        layer.FitRect(new Rect(-10f, -10f, 20f, 20f));

        // min(200/20, 150/20) = 7.5, a quarter of the 30 the full frame would have given.
        Assert.AreEqual(7.5f, layer.Camera.Zoom, 1e-4f);
        Assert.AreEqual(Vector2.Zero, layer.Camera.Position);
    }

    [TestMethod]
    public void TheFullFrameAndViewportFitsDisagreeOnTheSameScene()
    {
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var full = scene.Layers.Add(new WorldLayer2D());
        var cropped = scene.Layers.Add(new WorldLayer2D { Viewport = new Rect(0f, 0f, 200f, 150f) });
        var rect = new Rect(0f, 0f, 40f, 10f);

        full.FitRect(rect);
        cropped.FitRect(rect);

        // min(800/40, 600/10) = 20 against the frame; min(200/40, 150/10) = 5 against the viewport.
        Assert.AreEqual(20f, full.Camera.Zoom, 1e-4f);
        Assert.AreEqual(5f, cropped.Camera.Zoom, 1e-4f);
        Assert.AreNotEqual(full.Camera.Zoom, cropped.Camera.Zoom);

        // Both are exactly the two-argument call with the extent the layer frames, and nothing else.
        var twin = new WorldLayer2D();
        twin.Camera.FitRect(rect, new Vector2(200f, 150f));
        Assert.AreEqual(twin.Camera.Zoom, cropped.Camera.Zoom);
        Assert.AreEqual(twin.Camera.Position, cropped.Camera.Position);
    }

    [TestMethod]
    public void TheOneArgumentFitRectWorksBeforeTheSceneLoadsAndAfterItDoes()
    {
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = scene.Layers.Add(new WorldLayer2D());
        var rect = new Rect(-10f, -10f, 20f, 20f);

        layer.FitRect(rect);
        var beforeLoad = layer.Camera.Zoom;

        scene.Load(TestEnvironment.Stub());
        layer.Camera.Zoom = 1f;
        layer.FitRect(rect);

        Assert.AreEqual(30f, beforeLoad, 1e-4f);
        Assert.AreEqual(beforeLoad, layer.Camera.Zoom);
    }

    [TestMethod]
    public void AFullFrameLayerWithNoSceneRefusesToInventAResolution()
    {
        var orphan = new WorldLayer2D();

        var failure = Assert.ThrowsExactly<InvalidOperationException>(
            () => orphan.FitRect(new Rect(0f, 0f, 4f, 2f)));

        Assert.Contains("VirtualResolution", failure.Message, StringComparison.Ordinal);

        // A viewport'd layer owns its extent outright, so it has an honest answer without a scene.
        var viewported = new WorldLayer2D { Viewport = new Rect(0f, 0f, 40f, 20f) };

        viewported.FitRect(new Rect(0f, 0f, 4f, 2f));

        Assert.AreEqual(10f, viewported.Camera.Zoom, 1e-4f);
    }

    [TestMethod]
    public void TheOneArgumentFitRectRejectsADegenerateRectangle()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new WorldLayer2D());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => layer.FitRect(new Rect(0f, 0f, 4f, 0f)));
    }

    [TestMethod]
    public void TheWorldLayerReportsAYUpCoordinateSpace()
    {
        Assert.IsTrue(new WorldLayer2D().YAxisPointsUp);
        Assert.IsFalse(new ScreenLayer().YAxisPointsUp);
    }

    [TestMethod]
    public void AssigningAParentlessCameraAttachesItToTheLayerRoot()
    {
        var layer = new WorldLayer2D();
        var original = layer.Camera;
        var replacement = new Camera2D { Zoom = 2f };

        layer.Camera = replacement;

        Assert.AreSame(replacement, layer.Camera);
        Assert.AreSame(layer.Root, replacement.Parent);

        // The replaced camera keeps its place in the tree; the layer simply stops framing with it.
        Assert.AreSame(layer.Root, original.Parent);
    }

    [TestMethod]
    public void AssigningAParentedCameraLeavesItWhereItSits()
    {
        var layer = new WorldLayer2D();
        var rig = layer.Root.Add(new Node2D { Position = new Vector2(5f, 7f) });
        var mounted = rig.Add(new Camera2D());

        layer.Camera = mounted;

        Assert.AreSame(mounted, layer.Camera);
        Assert.AreSame(rig, mounted.Parent);
    }

    [TestMethod]
    public void ReassigningTheSameCameraIsANoOp()
    {
        var layer = new WorldLayer2D();
        var camera = layer.Camera;

        layer.Camera = camera;

        Assert.AreSame(camera, layer.Camera);
        Assert.AreSame(layer.Root, camera.Parent);
    }

    [TestMethod]
    public void AssigningANullCameraThrows()
    {
        var layer = new WorldLayer2D();
        var camera = layer.Camera;

        Assert.ThrowsExactly<ArgumentNullException>(() => layer.Camera = null!);
        Assert.AreSame(camera, layer.Camera);
    }
}
