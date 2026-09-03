using System.Numerics;
using System.Text;
using Najm.Tests;

namespace Najm.Core.Tests.Input;

/// <summary>
/// ARCHITECTURE §9.2's state machine — hover, capture, focus, consumption — and §9.3's dispatch
/// vocabulary.
/// </summary>
[TestClass]
public sealed class RouterDispatchTests
{
    private static readonly Rect Square = new(-50f, -50f, 100f, 100f);

    [TestMethod]
    public void APressReachesTheNodeUnderItInBothCoordinateSpaces()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square, position: new Vector2(300f, 400f));
        harness.Load();

        harness.Buffer.PressPointer(0, new Vector2(320f, 380f), PointerButton.Left, KeyModifiers.Shift);
        harness.Tick();

        CollectionAssert.AreEqual(new[] { "node:enter", "node:down" }, log);
        Assert.AreEqual(new Vector2(320f, 380f), node.LastPointer.Virtual);
        Assert.AreEqual(new Vector2(20f, -20f), node.LastPointer.Local);
        Assert.AreEqual(PointerButton.Left, node.LastPointer.Button);
        Assert.AreEqual(PointerButton.Left, node.LastPointer.Buttons);
        Assert.AreEqual(KeyModifiers.Shift, node.LastPointer.Modifiers);
        Assert.AreEqual(0, node.LastPointer.PointerId);
    }

    [TestMethod]
    public void HoverEntersAndExitsAsThePointerCrossesNodes()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        harness.Add("left", log, Square, position: new Vector2(100f, 100f));
        harness.Add("right", log, Square, position: new Vector2(400f, 100f));
        harness.Load();

        harness.Buffer.MovePointer(0, new Vector2(100f, 100f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "left:enter", "left:move" }, log);

        log.Clear();
        harness.Buffer.MovePointer(0, new Vector2(400f, 100f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "left:exit", "right:enter", "right:move" }, log);
        Assert.AreEqual("right", harness.Router.HoverTarget(0)!.ToString());

        log.Clear();
        harness.Buffer.MovePointer(0, new Vector2(900f, 900f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "right:exit" }, log);
        Assert.IsNull(harness.Router.HoverTarget(0));
    }

    [TestMethod]
    public void CaptureBypassesTheWalkAndSurvivesLeavingTheNode()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var handle = harness.Add("handle", log, Square, position: new Vector2(200f, 200f));
        var other = harness.Add("other", log, Square, position: new Vector2(800f, 200f));
        harness.Load();
        handle.PointerDown = (node, _) => harness.Router.Capture(node, 0);

        harness.Buffer.PressPointer(0, new Vector2(200f, 200f), PointerButton.Left);
        harness.Tick();
        Assert.AreSame(handle, harness.Router.CaptureHolder(0));

        // The pointer travels onto `other`, which never hears about it: §9.2's capture bypasses the
        // walk entirely, which is exactly what keeps a drag alive off the node.
        log.Clear();
        harness.Buffer.MovePointer(0, new Vector2(800f, 200f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "handle:drag" }, log);
        Assert.AreSame(handle, harness.Router.HoverTarget(0), "Hover follows the captured node.");

        log.Clear();
        harness.Buffer.ReleasePointer(0, new Vector2(800f, 200f), PointerButton.Left);
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "handle:up" }, log);

        Assert.IsTrue(harness.Router.ReleaseCapture(0));
        Assert.IsFalse(harness.Router.ReleaseCapture(0));
        Assert.IsNull(harness.Router.CaptureHolder(0));

        // With the capture gone the walk resumes and `other` finally sees the pointer.
        log.Clear();
        harness.Buffer.MovePointer(0, new Vector2(800f, 200f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "handle:exit", "other:enter", "other:move" }, log);
    }

    [TestMethod]
    public void AMoveIsADragOnlyWhileCapturedWithAButtonHeld()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square, position: new Vector2(200f, 200f));
        harness.Load();

        harness.Buffer.MovePointer(0, new Vector2(200f, 200f));
        harness.Tick();
        CollectionAssert.Contains(log, "node:move");

        // Captured, but no button down: still a move.
        log.Clear();
        harness.Router.Capture(node, 0);
        harness.Buffer.MovePointer(0, new Vector2(210f, 200f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "node:move" }, log);

        log.Clear();
        harness.Buffer.PressPointer(0, new Vector2(210f, 200f), PointerButton.Left);
        harness.Buffer.MovePointer(0, new Vector2(220f, 200f));
        harness.Tick();
        CollectionAssert.AreEqual(new[] { "node:down", "node:drag" }, log);
    }

    [TestMethod]
    public void DragDeltasArriveInTheReceivingNodesOwnUnitsUnderACamera()
    {
        var log = new List<string>();
        var scene = new Scene { VirtualResolution = new Vector2(800f, 600f) };
        var world = new WorldLayer2D();
        scene.Layers.Add(world);
        var node = new RecordingNode(new Rect(-10f, -10f, 20f, 20f)) { Name = "point", Log = log };
        world.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        try
        {
            world.Camera.Zoom = 2f;
            scene.Input.Capture(node, 0);

            var buffer = new InputBuffer();
            buffer.PressPointer(0, new Vector2(400f, 300f), PointerButton.Left);
            buffer.MovePointer(0, new Vector2(440f, 260f));
            scene.Tick(new TickContext(new TimeInfo(0.016, 0.016, 0, isFixedStep: false), buffer.Block));

            // Forty virtual units right and forty up, at zoom 2, is twenty world units right and
            // twenty up — and world Y is up, so the flip is part of the answer. A handler that adds
            // this to Position tracks the pointer exactly, without knowing the camera exists.
            Assert.AreEqual(new Vector2(40f, -40f), node.LastPointer.VirtualDelta);
            Assert.AreEqual(20f, node.LastPointer.LocalDelta.X, 1e-3f);
            Assert.AreEqual(20f, node.LastPointer.LocalDelta.Y, 1e-3f);
        }
        finally
        {
            scene.Unload();
        }
    }

    [TestMethod]
    public void KeysAndTextGoToTheFocusedNodeAndNowhereElse()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var field = harness.Add("field", log, Square);
        var other = harness.Add("other", log, Square, position: new Vector2(500f, 500f));
        harness.Load();

        // Nothing focused: the events are routed nowhere and stay available to polling.
        harness.Buffer.PressKey(Key.A);
        harness.Buffer.EnterText(new Rune('a'));
        harness.Tick();
        Assert.IsEmpty(log);

        harness.Router.Focus(field);
        CollectionAssert.AreEqual(new[] { "field:focus" }, log);

        log.Clear();
        harness.Buffer.PressKey(Key.B, KeyModifiers.Control);
        harness.Buffer.EnterText(new Rune('b'));
        harness.Buffer.ReleaseKey(Key.B, KeyModifiers.Control);
        harness.Tick();
        CollectionAssert.AreEqual(
            new[] { "field:key(B,down)", "field:text(b)", "field:key(B,up)" },
            log);
        Assert.AreEqual(KeyModifiers.Control, field.LastKey.Modifiers);
        Assert.AreEqual(new Rune('b'), field.LastText.Text);

        // Moving focus blurs the old holder first, and Focused already reads the new one.
        log.Clear();
        harness.Router.Focus(other);
        CollectionAssert.AreEqual(new[] { "field:blur", "other:focus" }, log);
        Assert.AreSame(other, harness.Router.Focused);

        log.Clear();
        harness.Router.Focus(other);
        Assert.IsEmpty(log, "Focusing the current holder does nothing.");

        harness.Router.Focus(null);
        CollectionAssert.AreEqual(new[] { "other:blur" }, log);
        Assert.IsNull(harness.Router.Focused);
    }

    [TestMethod]
    public void AHandledEventDisappearsFromPollingAndAnUnhandledOneDoesNot()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square, position: new Vector2(100f, 100f));
        harness.Load();

        var seenByPolling = new List<bool>();
        var probe = new PollingLayer(seenByPolling);
        harness.Scene.Layers.Add(probe);

        node.Handles = false;
        harness.Buffer.PressPointer(0, new Vector2(100f, 100f), PointerButton.Left);
        harness.Tick();
        Assert.IsTrue(seenByPolling[0], "An unhandled press is still there for a poller.");

        node.Handles = true;
        harness.Buffer.PressPointer(0, new Vector2(100f, 100f), PointerButton.Left);
        harness.Tick();
        Assert.IsFalse(seenByPolling[1], "A handled press is consumed and polling never sees it.");
    }

    [TestMethod]
    public void DetachReleasesCaptureFocusAndHoverWithoutNotifying()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square, position: new Vector2(100f, 100f));
        harness.Load();

        harness.Buffer.MovePointer(0, new Vector2(100f, 100f));
        harness.Tick();
        harness.Router.Capture(node, 0);
        harness.Router.Focus(node);
        Assert.AreSame(node, harness.Router.HoverTarget(0));

        log.Clear();
        harness.Layer.Root.Remove(node);
        harness.Tick();

        // §6.4 and §6.6: detach releases both, deterministically. Silently, because the node has
        // already had its OnDetach and a blur callback into it would be about nothing.
        Assert.IsNull(harness.Router.CaptureHolder(0));
        Assert.IsNull(harness.Router.Focused);
        Assert.IsNull(harness.Router.HoverTarget(0));
        Assert.IsEmpty(log);
    }

    [TestMethod]
    public void DetachingAnAncestorReleasesADescendantsCaptureToo()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var group = harness.Add("group", log, Square);
        var child = harness.Add("child", log, Square, group);
        harness.Load();

        harness.Router.Capture(child, 0);
        harness.Router.Focus(child);

        harness.Layer.Root.Remove(group);
        harness.Tick();

        Assert.IsNull(harness.Router.CaptureHolder(0));
        Assert.IsNull(harness.Router.Focused);
    }

    [TestMethod]
    public void CaptureAndFocusRefuseNodesThatCannotHoldThem()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square);
        var plain = harness.Layer.Root.Add(new HitNode(Square) { Name = "plain" });
        var detached = new RecordingNode(Square) { Name = "detached", Log = log };
        harness.Load();

        Assert.ThrowsExactly<ArgumentNullException>(() => harness.Router.Capture(null!, 0));
        Assert.ThrowsExactly<ArgumentException>(() => harness.Router.Capture(plain, 0));
        Assert.ThrowsExactly<ArgumentException>(() => harness.Router.Capture(detached, 0));
        Assert.ThrowsExactly<ArgumentException>(() => harness.Router.Focus(plain));
        Assert.ThrowsExactly<ArgumentException>(() => harness.Router.Focus(detached));

        harness.Router.Capture(node, 0);
        Assert.AreSame(node, harness.Router.CaptureHolder(0));
        Assert.IsNull(harness.Router.CaptureHolder(7), "An unknown pointer holds nothing.");
        Assert.IsNull(harness.Router.HoverTarget(7));
    }

    [TestMethod]
    public void EachPointerCarriesItsOwnCaptureAndHover()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var left = harness.Add("left", log, Square, position: new Vector2(100f, 100f));
        var right = harness.Add("right", log, Square, position: new Vector2(500f, 100f));
        harness.Load();

        harness.Router.Capture(left, 1);
        harness.Router.Capture(right, 2);

        log.Clear();
        harness.Buffer.MovePointer(1, new Vector2(999f, 999f));
        harness.Buffer.MovePointer(2, new Vector2(999f, 999f));
        harness.Tick();

        CollectionAssert.AreEqual(new[] { "left:enter", "left:move", "right:enter", "right:move" }, log);
        Assert.AreEqual(1, left.LastPointer.PointerId);
        Assert.AreEqual(2, right.LastPointer.PointerId);
    }

    [TestMethod]
    public void ANodeAttachedFromAnInputHandlerUpdatesTheSameFrame()
    {
        var log = new List<string>();
        using var harness = new RouterHarness();
        var node = harness.Add("node", log, Square, position: new Vector2(100f, 100f));
        harness.Load();

        var spawned = new UpdateCountingNode();
        node.PointerDown = (owner, _) => owner.Add(spawned);

        harness.Buffer.PressPointer(0, new Vector2(100f, 100f), PointerButton.Left);
        harness.Tick();

        // §6.4: the Input phase ends with its own flush, so an Input-added node participates in
        // every later phase of the same frame.
        Assert.AreEqual(1, spawned.Updates);
    }

    [TestMethod]
    public void AWarmRoutedFrameAllocatesNoManagedBytes()
    {
        // §9.1 and §3.6: the block is a readonly struct over pooled buffers, the args are structs
        // passed by `in`, and the router's per-pointer state is an array that grows only when a new
        // pointer id appears. The receiving node counts rather than logs, because a probe that
        // formats a string would be measuring the probe.
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var node = new CountingInteractiveNode { Position = new Vector2(300f, 300f) };
        layer.Root.Add(node);
        scene.Load(TestEnvironment.Stub());

        try
        {
            scene.Input.Capture(node, 0);
            scene.Input.Focus(node);

            var buffer = new InputBuffer(capacity: 64);
            var frame = 0L;
            var rune = new Rune('a');

            var reading = AllocationProbe.AssertNoneAllocated(
                500,
                () =>
                {
                    buffer.BeginFrame();
                    buffer.MovePointer(0, new Vector2(300f, 300f));
                    buffer.PressPointer(0, new Vector2(300f, 300f), PointerButton.Left);
                    buffer.MovePointer(0, new Vector2(310f, 300f));
                    buffer.ScrollPointer(0, new Vector2(310f, 300f), new Vector2(0f, 1f));
                    buffer.ReleasePointer(0, new Vector2(310f, 300f), PointerButton.Left);
                    buffer.PressKey(Key.A);
                    buffer.EnterText(rune);
                    buffer.ReleaseKey(Key.A);

                    scene.Tick(new TickContext(
                        new TimeInfo((frame + 1) * 0.016, 0.016, frame, isFixedStep: false),
                        buffer.Block));
                    frame++;
                },
                "A warm routed frame");

            Assert.AreEqual(reading.Invocations, frame);
            Assert.AreEqual(
                (reading.Invocations * 8) + 1,
                node.Dispatches,
                "Eight events per frame reached the node, plus the single hover enter of the first.");
        }
        finally
        {
            scene.Unload();
        }
    }

    [TestMethod]
    public void AWarmUnroutedWalkAllocatesNoManagedBytes()
    {
        var scene = new Scene();
        var layer = new ScreenLayer();
        scene.Layers.Add(layer);
        var group = layer.Root.Add(new Node2D());
        for (var index = 0; index < 32; index++)
        {
            group.Add(new CountingInteractiveNode { Position = new Vector2(index * 40f, 300f) });
        }

        scene.Load(TestEnvironment.Stub());
        try
        {
            var router = scene.Input;
            AllocationProbe.AssertNoneAllocated(
                2000,
                () => router.Pick(new Vector2(1500f, 900f)),
                "A warm miss over a 32-node tree");
        }
        finally
        {
            scene.Unload();
        }
    }

    private sealed class CountingInteractiveNode : Node2D, IInteractive
    {
        internal int Dispatches { get; private set; }

        public override Rect HitBounds => Square;

        void IInteractive.OnPointerEnter(in PointerArgs args) => Dispatches++;

        void IInteractive.OnPointerExit(in PointerArgs args) => Dispatches++;

        bool IInteractive.OnPointerDown(in PointerArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnPointerUp(in PointerArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnPointerMove(in PointerArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnDrag(in PointerArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnScroll(in PointerArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnKey(in KeyArgs args)
        {
            Dispatches++;
            return false;
        }

        bool IInteractive.OnTextInput(in TextInputArgs args)
        {
            Dispatches++;
            return false;
        }
    }

    private sealed class UpdateCountingNode : Node2D
    {
        internal int Updates { get; private set; }

        protected override void Update(in TickContext tick) => Updates++;
    }

    /// <summary>Polls for the same press the router may have consumed, from the Update phase.</summary>
    private sealed class PollingLayer(List<bool> seen) : ScreenLayer
    {
        protected override void Update(in TickContext tick) =>
            seen.Add(tick.Input.WasPressed(PointerButton.Left));
    }
}
