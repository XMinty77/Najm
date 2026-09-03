using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Tests.Input;

/// <summary>
/// ARCHITECTURE §9.2's camera- and pinning-resolved mapping, and §6.3's rule that it is the layer —
/// never <c>WorldMatrix</c> — that knows it.
/// </summary>
[TestClass]
public sealed class ResolvedNodeFrameTests
{
    [TestMethod]
    public void AScreenLayerNodeResolvesThroughItsWorldMatrixAlone()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(-5f, -5f, 10f, 10f)) { Position = new Vector2(100f, 200f) };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        var frame = layer.Resolve(node);

        // A ScreenLayer's coordinates already are virtual coordinates, so there is nothing between
        // the node's world matrix and the answer.
        Assert.AreEqual(node.WorldMatrix, frame.LocalToVirtualMatrix);
        Assert.AreEqual(new Rect(95f, 195f, 10f, 10f), frame.HitBoundsVirtual);
        Assert.AreEqual(new Vector2(100f, 200f), frame.LocalToVirtual(Vector2.Zero));
        Assert.AreEqual(Vector2.Zero, frame.VirtualToLocal(new Vector2(100f, 200f)));
        Assert.IsTrue(frame.IsMappable);

        scene.Unload();
    }

    [TestMethod]
    public void AWorldLayerNodeResolvesThroughTheCameraIncludingTheYFlipAndTheZoom()
    {
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = new WorldLayer2D();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(-1f, -1f, 2f, 2f)) { Position = new Vector2(10f, 20f) };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        layer.Camera.Zoom = 3f;
        var frame = layer.Resolve(node);

        // The camera sits at the world origin, which lands on the virtual centre (400, 300). World
        // (10, 20) at zoom 3 is 30 virtual units right and — Y being flipped exactly here — 60 up.
        AssertPoint(new Vector2(430f, 240f), frame.LocalToVirtual(Vector2.Zero));

        // Local (-1,-1) is the node's top-left in a Y-up world, so it maps to the bottom-left in
        // virtual space: the hull spans x in [427,433] and y in [237,243].
        AssertRect(new Rect(427f, 237f, 6f, 6f), frame.HitBoundsVirtual);

        AssertPoint(Vector2.Zero, frame.VirtualToLocal(new Vector2(430f, 240f)));
        AssertPoint(new Vector2(1f, 1f), frame.VirtualToLocal(new Vector2(433f, 237f)));

        scene.Unload();
    }

    [TestMethod]
    public void TheResolvedMappingCarriesNoRenderScaleSoItIsTheSameAtEveryOutputSize()
    {
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = new WorldLayer2D();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(0f, 0f, 4f, 4f)) { Position = new Vector2(3f, 7f) };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        var resolved = layer.ResolveMatrix(node);
        var expected = node.WorldMatrix *
            RenderTraverser.ComputeLayerBase(layer, scene.VirtualResolution, renderScale: 1f);

        AssertMatrix(expected, resolved);

        scene.Unload();
    }

    [TestMethod]
    public void AViewportdWorldLayerResolvesThroughItsOwnExtentAndOrigin()
    {
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var layer = new WorldLayer2D { Viewport = new Rect(400f, 0f, 400f, 600f) };
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(-1f, -1f, 2f, 2f));
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        // The camera frames the viewport, whose centre is viewport-local (200, 300); the viewport's
        // origin carries that to frame virtual (600, 300). Framing the scene's 800x600 instead
        // would put the world origin at (400, 300) and every pointer test would miss by 200.
        AssertPoint(new Vector2(600f, 300f), layer.Resolve(node).LocalToVirtual(Vector2.Zero));

        scene.Unload();
    }

    [TestMethod]
    public void ARotatedNodeResolvesToTheAxisAlignedHullAndStillMapsPointsExactly()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(-10f, -20f, 20f, 40f))
        {
            Position = new Vector2(500f, 500f),
            Rotation = Angle.Deg(90f),
        };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        var frame = layer.Resolve(node);

        // A quarter turn swaps the extents: the 20x40 rectangle becomes 40x20 about (500, 500).
        AssertRect(new Rect(480f, 490f, 40f, 20f), frame.HitBoundsVirtual);

        // The hull is a gate, not an answer. The exact local test is what rejects the corners.
        AssertPoint(new Vector2(0f, 20f), frame.VirtualToLocal(new Vector2(480f, 500f)));
        Assert.IsTrue(node.HitTest(frame.VirtualToLocal(new Vector2(500f, 500f))));

        scene.Unload();
    }

    [TestMethod]
    public void VisualAndHitBoundsResolveIndependently()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(0f, 0f, 10f, 10f))
        {
            Visual = new Rect(-5f, -5f, 20f, 20f),
            Position = new Vector2(100f, 100f),
        };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        var frame = layer.Resolve(node);

        Assert.AreEqual(new Rect(100f, 100f, 10f, 10f), frame.HitBoundsVirtual);
        Assert.AreEqual(new Rect(95f, 95f, 20f, 20f), frame.VisualBoundsVirtual);

        // ResolveBounds answers with the visual value, because culling and measurement are what
        // section 6.3 names as its consumers.
        Assert.AreEqual(frame.VisualBoundsVirtual, layer.ResolveBounds(node));

        scene.Unload();
    }

    [TestMethod]
    public void ACollapsedNodeIsNotMappableAndResolvesToNothing()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var node = new HitNode(new Rect(0f, 0f, 10f, 10f)) { Scale = new Vector2(0f, 1f) };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        var frame = layer.Resolve(node);

        Assert.IsFalse(frame.IsMappable);
        Assert.AreEqual(Matrix3x2.Identity, frame.VirtualToLocalMatrix);
        Assert.AreEqual(0f, frame.HitBoundsVirtual.Width);
        Assert.IsTrue(frame.HitBoundsVirtual.IsEmpty, "An empty gate can never be entered.");
        Assert.ThrowsExactly<InvalidOperationException>(() => frame.VirtualToLocal(Vector2.Zero));

        scene.Unload();
    }

    [TestMethod]
    public void ResolvingRefusesAForeignNodeAndASceneLessLayer()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        var other = new ScreenLayer();
        scene.Layers.Add(layer);
        scene.Layers.Add(other);
        var node = new HitNode(new Rect(0f, 0f, 1f, 1f));
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        Assert.ThrowsExactly<ArgumentNullException>(() => layer.Resolve(null!));
        Assert.ThrowsExactly<ArgumentException>(() => other.Resolve(node));
        Assert.ThrowsExactly<ArgumentException>(() => layer.Resolve(new HitNode(default)));

        // A layer that belongs to no scene has no virtual resolution to frame against, and says so
        // rather than inventing 1920x1080. Ownership is structural, so the node passes and the
        // missing scene is what fails.
        var orphan = new ScreenLayer();
        var orphanNode = new HitNode(default);
        orphan.Root.Add(orphanNode);
        Assert.ThrowsExactly<InvalidOperationException>(() => orphan.Resolve(orphanNode));

        // The same layer inside a constructed but unloaded scene resolves, because the stack knows
        // the resolution even before load does.
        var unloaded = new Scene { VirtualResolution = new Vector2(640f, 480f) };
        unloaded.Layers.Add(orphan);
        Assert.AreEqual(Matrix3x2.Identity, orphan.ResolveMatrix(orphanNode));

        scene.Unload();
    }

    [TestMethod]
    public void HitTestDefaultsToTheDeclaredHitBoundsAndAnOverrideNarrowsIt()
    {
        var rectangular = new HitNode(new Rect(-4f, -4f, 8f, 8f));

        Assert.IsTrue(rectangular.HitTest(Vector2.Zero));
        Assert.IsTrue(rectangular.HitTest(new Vector2(-4f, -4f)));
        Assert.IsFalse(rectangular.HitTest(new Vector2(4f, 0f)));
        Assert.IsFalse(rectangular.HitTest(new Vector2(5f, 5f)));

        // A plain node declares nothing and is therefore hit by nothing, including its own origin.
        Assert.IsFalse(new Node2D().HitTest(Vector2.Zero));

        var disc = new DiscNode(radius: 4f);
        Assert.IsTrue(disc.HitTest(new Vector2(3f, 2f)));
        Assert.IsFalse(
            disc.HitTest(new Vector2(3.5f, 3.5f)),
            "The corner is inside the bounding box and outside the disc; that is the point of the override.");
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-3f, $"x of {actual}");
        Assert.AreEqual(expected.Y, actual.Y, 1e-3f, $"y of {actual}");
    }

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-3f, $"x of {actual}");
        Assert.AreEqual(expected.Y, actual.Y, 1e-3f, $"y of {actual}");
        Assert.AreEqual(expected.Width, actual.Width, 1e-3f, $"width of {actual}");
        Assert.AreEqual(expected.Height, actual.Height, 1e-3f, $"height of {actual}");
    }

    private static void AssertMatrix(Matrix3x2 expected, Matrix3x2 actual)
    {
        Assert.AreEqual(expected.M11, actual.M11, 1e-4f);
        Assert.AreEqual(expected.M12, actual.M12, 1e-4f);
        Assert.AreEqual(expected.M21, actual.M21, 1e-4f);
        Assert.AreEqual(expected.M22, actual.M22, 1e-4f);
        Assert.AreEqual(expected.M31, actual.M31, 1e-4f);
        Assert.AreEqual(expected.M32, actual.M32, 1e-4f);
    }

    private sealed class DiscNode(float radius) : Node2D
    {
        public override Rect HitBounds => new(-radius, -radius, radius * 2f, radius * 2f);

        public override bool HitTest(Vector2 local) => local.Length() <= radius;
    }
}
