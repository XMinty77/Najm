using System.Numerics;

namespace Najm.Core.Tests.Input;

/// <summary>
/// ARCHITECTURE §9.2's traversal order, pinned as an order rather than as an outcome: every test
/// here asserts the full sequence of nodes the walk asked, so a reversed or flattened traversal
/// fails even when it happens to reach the same answer.
/// </summary>
[TestClass]
public sealed class RouterOrderTests
{
    private static readonly Rect Overlapping = new(-50f, -50f, 100f, 100f);

    [TestMethod]
    public void TheWalkIsTheExactReverseOfDepthFirstPaintOrder()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();

        // Paint order is depth-first pre-order with parents beneath their children (§6.5), so this
        // tree paints A, A1, A2, B, B1, B2 and must be walked B2, B1, B, A2, A1, A.
        var a = harness.Add("A", log, Overlapping, hitLog: log);
        harness.Add("A1", log, Overlapping, a, hitLog: log);
        harness.Add("A2", log, Overlapping, a, hitLog: log);
        var b = harness.Add("B", log, Overlapping, hitLog: log);
        harness.Add("B1", log, Overlapping, b, hitLog: log);
        harness.Add("B2", log, Overlapping, b, hitLog: log);
        foreach (var node in new[] { a, b })
        {
            node.Solid = false;
        }
        harness.Load();
        foreach (var node in harness.Layer.Root.Children)
        {
            MakeTransparent(node);
        }

        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(new[] { "B2", "B1", "B", "A2", "A1", "A" }, log);
    }

    [TestMethod]
    public void ZIndexReordersTheWalkExactlyAsItReordersPaint()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var first = harness.Add("first", log, Overlapping, hitLog: log);
        var second = harness.Add("second", log, Overlapping, hitLog: log);
        var third = harness.Add("third", log, Overlapping, hitLog: log);
        harness.Load();
        foreach (var node in new[] { first, second, third })
        {
            node.Solid = false;
        }

        // Insertion order alone: paints first, second, third; walked in reverse.
        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(new[] { "third", "second", "first" }, log);

        // §6.7's stable sort by (ZIndex, insertion index). Raising `first` above the others makes it
        // paint last and therefore be asked first, while `second` and `third` keep their insertion
        // tie-break.
        log.Clear();
        first.ZIndex = 5;
        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(new[] { "first", "third", "second" }, log);

        // A negative index sinks a node beneath its siblings, so it is asked last.
        log.Clear();
        first.ZIndex = 0;
        third.ZIndex = -1;
        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(new[] { "second", "first", "third" }, log);
    }

    [TestMethod]
    public void LayersAreWalkedTopToBottomWhichIsTheReverseOfAddOrder()
    {
        var log = new List<string>();
        var scene = new Scene();
        var bottom = new ScreenLayer();
        var middle = new ScreenLayer();
        var top = new ScreenLayer();
        scene.Layers.Add(bottom);
        scene.Layers.Add(middle);
        scene.Layers.Add(top);

        foreach (var (layer, name) in new[] { (bottom, "bottom"), (middle, "middle"), (top, "top") })
        {
            layer.Root.Add(new RecordingNode(Overlapping)
            {
                Name = name,
                Log = log,
                HitLog = log,
                Solid = false,
            });
        }

        scene.Load(TestEnvironment.Stub());
        try
        {
            // §5.2 composites layers back-to-front in add order, so the last added is on top, and
            // §9.2 visits them top-to-bottom.
            Assert.IsNull(scene.Input.Pick(Vector2.Zero));
            CollectionAssert.AreEqual(new[] { "top", "middle", "bottom" }, log);
        }
        finally
        {
            scene.Unload();
        }
    }

    [TestMethod]
    public void TheFirstNodeThatAcceptsEndsTheWalk()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var under = harness.Add("under", log, Overlapping, hitLog: log);
        var over = harness.Add("over", log, Overlapping, hitLog: log);
        harness.Load();
        under.Solid = true;
        over.Solid = true;

        Assert.AreSame(over, harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(
            new[] { "over" },
            log,
            "The topmost node accepted, so nothing beneath it was ever asked.");
    }

    [TestMethod]
    public void ANonInteractiveNodeDoesNotBlockAnInteractiveOneBeneathIt()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var interactive = harness.Add("interactive", log, Overlapping, hitLog: log);
        harness.Layer.Root.Add(new HitNode(Overlapping) { Name = "decoration", HitLog = log });
        harness.Load();

        // §9.3 makes IInteractive opt-in, so a node that cannot receive anything is transparent to
        // the walk — and is never even hit-tested, which is why it does not appear in the log.
        Assert.AreSame(interactive, harness.Router.Pick(Vector2.Zero));
        CollectionAssert.AreEqual(new[] { "interactive" }, log);
    }

    [TestMethod]
    public void DisabledSkipsTheSubtreeForInputAndInvisibleSkipsItForHitTesting()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var group = harness.Add("group", log, Overlapping, hitLog: log);
        var child = harness.Add("child", log, Overlapping, group, hitLog: log);
        harness.Load();
        group.Solid = false;

        Assert.AreSame(child, harness.Router.Pick(Vector2.Zero));

        // §6.1: Enabled = false skips the subtree for Update *and* input, and still renders.
        log.Clear();
        group.Enabled = false;
        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        Assert.IsEmpty(log);

        // §6.1: Visible = false skips the subtree for Render *and* hit-testing, and still updates.
        group.Enabled = true;
        group.Visible = false;
        Assert.IsNull(harness.Router.Pick(Vector2.Zero));
        Assert.IsEmpty(log);

        group.Visible = true;
        Assert.AreSame(child, harness.Router.Pick(Vector2.Zero));
    }

    [TestMethod]
    public void ClipGatesTheWalkExactlyInTheClippingNodesOwnCoordinates()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var group = harness.Add("group", log, Overlapping, position: new Vector2(500f, 500f), hitLog: log);
        var child = harness.Add("child", log, new Rect(-100f, -100f, 200f, 200f), group, hitLog: log);
        harness.Load();
        group.Solid = false;

        // The child reaches 100 local units out; the clip admits only 20 of them. §9.2: the clip
        // gates the walk, and it is stated in the clipping node's local coordinates.
        group.Clip = new Rect(-20f, -20f, 40f, 40f);

        Assert.AreSame(child, harness.Router.Pick(new Vector2(515f, 500f)));
        Assert.IsNull(harness.Router.Pick(new Vector2(560f, 500f)));

        log.Clear();
        Assert.IsNull(harness.Router.Pick(new Vector2(560f, 500f)));
        Assert.IsEmpty(log, "A clipped-out subtree is not walked, so nothing in it is hit-tested.");

        // The clip travels with the node, so moving the group moves what it admits.
        group.Position = new Vector2(560f, 500f);
        Assert.AreSame(child, harness.Router.Pick(new Vector2(560f, 500f)));
    }

    [TestMethod]
    public void ALayerThatDoesNotParticipateIsSkippedWhole()
    {
        var log = new List<string>();
        var scene = new Scene();
        var lower = new ScreenLayer();
        var upper = new ScreenLayer();
        scene.Layers.Add(lower);
        scene.Layers.Add(upper);
        var beneath = new RecordingNode(Overlapping) { Name = "beneath", Log = log, HitLog = log };
        var above = new RecordingNode(Overlapping) { Name = "above", Log = log, HitLog = log };
        lower.Root.Add(beneath);
        upper.Root.Add(above);
        scene.Load(TestEnvironment.Stub());

        try
        {
            Assert.AreSame(above, scene.Input.Pick(Vector2.Zero));

            upper.ReceivesInput = false;
            Assert.AreSame(beneath, scene.Input.Pick(Vector2.Zero));

            upper.ReceivesInput = true;
            upper.Visible = false;
            Assert.AreSame(
                beneath,
                scene.Input.Pick(Vector2.Zero),
                "An invisible layer is not there to be clicked, by the same rule as an invisible node.");

            // Zero opacity is deliberately not the same case: the layer is present and transparent.
            upper.Visible = true;
            upper.Opacity = 0f;
            Assert.AreSame(above, scene.Input.Pick(Vector2.Zero));
            Assert.IsFalse(RenderTraverser.ParticipatesInRender(upper));
            Assert.IsTrue(InputRouter.ParticipatesInInput(upper));

            Assert.ThrowsExactly<ArgumentNullException>(() => InputRouter.ParticipatesInInput(null!));
        }
        finally
        {
            scene.Unload();
        }
    }

    [TestMethod]
    public void AViewportdLayerOnlyClaimsPointsInsideItsRegion()
    {
        var log = new List<string>();
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var panel = new ScreenLayer { Viewport = new Rect(400f, 0f, 400f, 600f) };
        scene.Layers.Add(panel);
        var node = new RecordingNode(new Rect(0f, 0f, 800f, 600f))
        {
            Name = "panel",
            Log = log,
            HitLog = log,
        };
        panel.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        try
        {
            Assert.AreSame(node, scene.Input.Pick(new Vector2(500f, 300f)));

            log.Clear();
            Assert.IsNull(scene.Input.Pick(new Vector2(100f, 300f)));
            Assert.IsEmpty(log, "A point outside the viewport never belonged to this layer.");
        }
        finally
        {
            scene.Unload();
        }
    }

    private static void MakeTransparent(Node node)
    {
        if (node is HitNode hit)
        {
            hit.Solid = false;
        }

        foreach (var child in node.Children)
        {
            MakeTransparent(child);
        }
    }
}
