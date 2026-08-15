namespace Najm.Core.Tests.Runtime;

[TestClass]
public sealed class AttachmentAndTraversalTests
{
    [TestMethod]
    public void AttachFullyRegistersSubtreeBeforeLayerNodeAndBehaviorCallbacks()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var rootBehavior = layer.Root.Behaviors.Add(new ProbeBehavior("rootBehavior", events));
        var parent = layer.Root.Add(new ProbeNode("parent", events));
        var parentBehavior = parent.Behaviors.Add(new ProbeBehavior("parentBehavior", events));
        var child = parent.Add(new ProbeNode("child", events));

        void AssertCoherentAttachment()
        {
            Assert.AreSame(scene, layer.AttachedScene);
            Assert.AreSame(layer, layer.Root.Layer);
            Assert.AreSame(layer, parent.Layer);
            Assert.AreSame(layer, child.Layer);
            Assert.AreEqual(3, scene.Registry.Count);
            Assert.IsTrue(scene.Registry.Contains(layer.Root));
            Assert.IsTrue(scene.Registry.Contains(parent));
            Assert.IsTrue(scene.Registry.Contains(child));
        }

        layer.AttachAction = _ => AssertCoherentAttachment();
        rootBehavior.AttachAction = AssertCoherentAttachment;
        parent.AttachAction = AssertCoherentAttachment;
        parentBehavior.AttachAction = AssertCoherentAttachment;
        child.AttachAction = AssertCoherentAttachment;

        scene.Load();

        CollectionAssert.AreEqual(
            new[]
            {
                "layer.attach",
                "rootBehavior.attach",
                "parent.attach",
                "parentBehavior.attach",
                "child.attach",
                "scene.load",
            },
            events);
    }

    [TestMethod]
    public void FailedAttachCompensatesOnlyCompletedHooksInReverseOrder()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        layer.Root.Behaviors.Add(new ProbeBehavior("rootBehavior", events));
        var failing = layer.Root.Add(new ProbeNode("failing", events)
        {
            AttachAction = () => throw new AttachFailureException(),
        });

        Assert.ThrowsExactly<AttachFailureException>(scene.Load);

        CollectionAssert.AreEqual(
            new[]
            {
                "layer.attach",
                "rootBehavior.attach",
                "failing.attach",
                "rootBehavior.detach",
                "layer.detach",
            },
            events);
        Assert.AreEqual(0, scene.Registry.Count);
        Assert.IsNull(layer.AttachedScene);
        Assert.IsNull(layer.Root.Layer);
        Assert.IsNull(failing.Layer);
        Assert.AreSame(layer.Root, failing.Parent);
        Assert.HasCount(1, scene.Layers);
    }

    [TestMethod]
    public void ThrowingDetachCallbacksSeeOldTopologyAndDoNotBlockCleanup()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var rootBehavior = layer.Root.Behaviors.Add(new ProbeBehavior("rootBehavior", events));
        var child = layer.Root.Add(new ProbeNode("child", events));
        var childBehavior = child.Behaviors.Add(new ProbeBehavior("childBehavior", events));

        void AssertOldTopology()
        {
            Assert.AreSame(layer.Root, child.Parent);
            Assert.AreSame(layer, child.Layer);
            Assert.AreSame(child, childBehavior.Node);
            Assert.IsTrue(scene.Registry.Contains(child));
        }

        childBehavior.DetachAction = () =>
        {
            AssertOldTopology();
            throw new DetachFailureException("behavior");
        };
        child.DetachAction = () =>
        {
            AssertOldTopology();
            throw new DetachFailureException("node");
        };
        rootBehavior.DetachAction = () => Assert.IsTrue(scene.Registry.Contains(child));
        layer.DetachAction = () => Assert.IsTrue(scene.Registry.Contains(child));
        scene.Load();
        events.Clear();

        var aggregate = Assert.ThrowsExactly<AggregateException>(scene.Unload);

        Assert.HasCount(2, aggregate.InnerExceptions);
        CollectionAssert.AreEqual(
            new[]
            {
                "scene.unload",
                "childBehavior.detach",
                "child.detach",
                "rootBehavior.detach",
                "layer.detach",
            },
            events);
        Assert.AreEqual(0, scene.Registry.Count);
        Assert.IsNull(child.Layer);
        Assert.AreSame(layer.Root, child.Parent);
        Assert.AreSame(child, childBehavior.Node);
        Assert.IsNull(layer.AttachedScene);
    }

    [TestMethod]
    public void BehaviorOwnershipIsImmediateWhileDetachedAndIndependentOfSceneDetach()
    {
        var node = new Node2D();
        var other = new Node2D();
        var behavior = node.Behaviors.Add(new ProbeBehavior("behavior", []));

        Assert.AreSame(node, behavior.Node);
        Assert.ThrowsExactly<InvalidOperationException>(() => node.Behaviors.Add(behavior));
        Assert.ThrowsExactly<InvalidOperationException>(() => other.Behaviors.Add(behavior));
        Assert.IsTrue(node.Behaviors.Remove(behavior));
        Assert.IsNull(behavior.Node);
        Assert.IsFalse(node.Behaviors.Remove(behavior));

        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new ScreenLayer());
        layer.Root.Behaviors.Add(behavior);
        scene.Load();
        scene.Unload();

        Assert.AreSame(layer.Root, behavior.Node);
        Assert.IsNull(layer.Root.Layer);
    }

    [TestMethod]
    public void LayerStackControlsIdentityOwnershipAndPermanentRoots()
    {
        var firstScene = new ProbeScene();
        var secondScene = new ProbeScene();
        var layer = firstScene.Layers.Add(new ScreenLayer());

        Assert.AreSame(layer, firstScene.Layers[0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => firstScene.Layers.Add(layer));
        Assert.ThrowsExactly<InvalidOperationException>(() => secondScene.Layers.Add(layer));
        Assert.ThrowsExactly<InvalidOperationException>(() => new Node2D().Add(layer.Root));
        Assert.IsTrue(firstScene.Layers.Remove(layer));
        Assert.IsFalse(firstScene.Layers.Remove(layer));

        secondScene.Layers.Add(layer);
        Assert.AreSame(layer, secondScene.Layers[0]);
        Assert.IsNull(layer.Root.Parent);
    }

    [TestMethod]
    public void LayerYAxisConventionIsVirtualAndScreenSpaceIsAlwaysDownward()
    {
        var customLayer = new UpAxisLayer();
        var screenLayer = new ScreenLayer();

        Assert.IsTrue(customLayer.YAxisPointsUp);
        Assert.IsFalse(screenLayer.YAxisPointsUp);
        Assert.IsTrue(
            typeof(ScreenLayer).GetProperty(nameof(Layer.YAxisPointsUp))!.GetMethod!.IsFinal);
    }

    [TestMethod]
    public void ExternalStyleCustomLayerRootAttachesDeferredChildrenThroughItsScene()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ExternalStyleLayer(events));
        scene.Load();
        events.Clear();

        var immediate = layer.Root.Add(new ProbeNode("immediate", events));

        Assert.AreSame(layer.Root, immediate.Parent);
        Assert.AreSame(layer, immediate.Layer);
        Assert.IsTrue(scene.Registry.Contains(immediate));
        CollectionAssert.AreEqual(new[] { "immediate.attach" }, events);

        var deferred = new ProbeNode("deferred", events);
        var addDeferred = true;
        layer.UpdateAction = () =>
        {
            if (!addDeferred)
            {
                return;
            }

            addDeferred = false;
            layer.Root.Add(deferred);
            Assert.IsNull(deferred.Parent);
            Assert.IsNull(deferred.Layer);
        };
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        CollectionAssert.AreEqual(
            new[] { "scene.start", "layer.update", "immediate.update", "deferred.attach" },
            events);
        Assert.AreSame(layer.Root, deferred.Parent);
        Assert.AreSame(layer, deferred.Layer);
        Assert.IsTrue(scene.Registry.Contains(deferred));
        Assert.AreEqual(3, scene.Registry.Count);

        events.Clear();
        scene.Tick(RuntimeTicks.At(1));
        CollectionAssert.AreEqual(
            new[] { "layer.update", "immediate.update", "deferred.update" },
            events);
    }

    [TestMethod]
    public void CustomLayerRootIdentityChangesFailLoudlyAndRetainCleanupRoot()
    {
        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new MutableRootLayer());
        var establishedRoot = layer.Root;
        scene.Load();
        layer.ReplaceRoot();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => scene.Tick(RuntimeTicks.At(0)));

        StringAssert.Contains(exception.Message, "root node identity cannot change");
        Assert.AreEqual(SceneState.Faulted, scene.State);
        Assert.AreSame(layer, establishedRoot.Layer);
        Assert.IsTrue(scene.Registry.Contains(establishedRoot));

        scene.Unload();
        Assert.AreEqual(SceneState.Unloaded, scene.State);
        Assert.IsNull(establishedRoot.Layer);
        Assert.IsNull(layer.Root.Layer);
        Assert.AreEqual(0, scene.Registry.Count);
    }

    [TestMethod]
    public void UpdateUsesLayerPreorderBehaviorAndInsertionOrder()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var firstLayer = scene.Layers.Add(new ProbeLayer("firstLayer", events));
        var first = firstLayer.Root.Add(new ProbeNode("first", events));
        first.Behaviors.Add(new ProbeBehavior("firstBehavior", events));
        first.Add(new ProbeNode("grandchild", events));
        var second = firstLayer.Root.Add(new ProbeNode("second", events)
        {
            Visible = false,
        });
        var secondLayer = scene.Layers.Add(new ProbeLayer("secondLayer", events)
        {
            Visible = false,
        });
        secondLayer.Root.Add(new ProbeNode("third", events));
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        CollectionAssert.AreEqual(
            new[]
            {
                "scene.start",
                "firstLayer.update",
                "first.update",
                "firstBehavior.update",
                "grandchild.update",
                "second.update",
                "secondLayer.update",
                "third.update",
            },
            events);

        first.Enabled = false;
        events.Clear();
        scene.Tick(RuntimeTicks.At(1));
        CollectionAssert.AreEqual(
            new[]
            {
                "firstLayer.update",
                "second.update",
                "secondLayer.update",
                "third.update",
            },
            events);
    }

    private sealed class AttachFailureException : Exception;

    private sealed class DetachFailureException(string message) : Exception(message);

    private sealed class UpAxisLayer : Layer
    {
        private readonly Node2D root = new();

        public override bool YAxisPointsUp => true;

        protected override Node RootNode => root;
    }

    private sealed class ExternalStyleLayer(List<string> events) : Layer
    {
        internal Node2D Root { get; } = new();

        internal Action? UpdateAction { get; set; }

        protected override Node RootNode => Root;

        protected override void Update(in TickContext tick)
        {
            events.Add("layer.update");
            UpdateAction?.Invoke();
        }
    }

    private sealed class MutableRootLayer : Layer
    {
        internal Node2D Root { get; private set; } = new();

        protected override Node RootNode => Root;

        internal void ReplaceRoot() => Root = new Node2D();
    }
}
