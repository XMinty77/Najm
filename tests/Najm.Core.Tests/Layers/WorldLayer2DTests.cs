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
