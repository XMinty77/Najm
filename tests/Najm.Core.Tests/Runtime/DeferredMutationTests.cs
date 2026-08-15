namespace Najm.Core.Tests.Runtime;

[TestClass]
public sealed class DeferredMutationTests
{
    [TestMethod]
    public void RemovedItemsFinishCurrentUpdateAndAdditionsStartNextTick()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var mutator = layer.Root.Add(new ProbeNode("mutator", events));
        var victim = layer.Root.Add(new ProbeNode("victim", events));
        var added = new ProbeNode("added", events);
        var mutate = true;
        mutator.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            Assert.IsTrue(layer.Root.Remove(victim));
            layer.Root.Add(added);
            Assert.AreSame(layer.Root, victim.Parent);
            Assert.IsNull(added.Parent);
        };
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        CollectionAssert.AreEqual(
            new[]
            {
                "scene.start",
                "layer.update",
                "mutator.update",
                "victim.update",
                "victim.detach",
                "added.attach",
            },
            events);
        Assert.IsNull(victim.Parent);
        Assert.AreSame(layer.Root, added.Parent);

        events.Clear();
        scene.Tick(RuntimeTicks.At(1));
        CollectionAssert.AreEqual(
            new[] { "layer.update", "mutator.update", "added.update" },
            events);
    }

    [TestMethod]
    public void ProjectedOwnershipMakesRepeatedRemoveFalseAndSamePhaseReparentLegal()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ScreenLayer());
        var firstParent = layer.Root.Add(new ProbeNode("firstParent", events));
        var child = firstParent.Add(new ProbeNode("child", events));
        var secondParent = layer.Root.Add(new ProbeNode("secondParent", events));
        var mutate = true;
        firstParent.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            Assert.IsTrue(firstParent.Remove(child));
            Assert.IsFalse(firstParent.Remove(child));
            secondParent.Add(child);
            Assert.AreSame(firstParent, child.Parent);
        };
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        Assert.AreSame(secondParent, child.Parent);
        Assert.AreSame(layer, child.Layer);
        Assert.IsTrue(scene.Registry.Contains(child));
        Assert.AreEqual(4, scene.Registry.Count);
        Assert.AreEqual(1, events.Count(item => item == "child.detach"));
        Assert.AreEqual(1, events.Count(item => item == "child.attach"));
    }

    [TestMethod]
    public void ProjectedCyclesAndForeignClaimsAreRejectedBeforeFlush()
    {
        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var parent = layer.Root.Add(new ProbeNode("parent", []));
        var pendingChild = new ProbeNode("pendingChild", []);
        var foreignParent = new Node2D();
        var mutate = true;
        parent.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            Assert.IsTrue(layer.Root.Remove(parent));
            parent.Add(pendingChild);
            Assert.ThrowsExactly<InvalidOperationException>(() => pendingChild.Add(parent));
            Assert.ThrowsExactly<InvalidOperationException>(() => foreignParent.Add(pendingChild));
            layer.Root.Add(parent);
            Assert.AreSame(layer.Root, parent.Parent);
            Assert.IsNull(pendingChild.Parent);
        };
        scene.Load();

        scene.Tick(RuntimeTicks.At(0));

        Assert.AreSame(layer.Root, parent.Parent);
        Assert.AreSame(parent, pendingChild.Parent);
        Assert.AreSame(layer, pendingChild.Layer);
        Assert.IsNull(parent.ReservationSink);
        Assert.IsNull(pendingChild.ReservationSink);
    }

    [TestMethod]
    public void BehaviorAndLayerEditsShareDeferredFifoAndOldItemsStillUpdate()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var firstLayer = scene.Layers.Add(new ProbeLayer("firstLayer", events));
        var oldBehavior = firstLayer.Root.Behaviors.Add(new ProbeBehavior("oldBehavior", events));
        var newBehavior = new ProbeBehavior("newBehavior", events);
        var secondLayer = scene.Layers.Add(new ProbeLayer("secondLayer", events));
        var addedLayer = new ProbeLayer("addedLayer", events);
        var mutate = true;
        firstLayer.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            Assert.IsTrue(firstLayer.Root.Behaviors.Remove(oldBehavior));
            firstLayer.Root.Behaviors.Add(newBehavior);
            Assert.IsTrue(scene.Layers.Remove(secondLayer));
            scene.Layers.Add(addedLayer);
            Assert.AreSame(firstLayer.Root, oldBehavior.Node);
            Assert.IsNull(newBehavior.Node);
            Assert.AreSame(scene.Layers, secondLayer.OwnerStack);
            Assert.IsNull(addedLayer.OwnerStack);
        };
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        CollectionAssert.AreEqual(
            new[]
            {
                "scene.start",
                "firstLayer.update",
                "oldBehavior.update",
                "secondLayer.update",
                "oldBehavior.detach",
                "newBehavior.attach",
                "secondLayer.detach",
                "addedLayer.attach",
            },
            events);
        Assert.IsNull(oldBehavior.Node);
        Assert.AreSame(firstLayer.Root, newBehavior.Node);
        Assert.IsNull(secondLayer.OwnerStack);
        Assert.AreSame(scene.Layers, addedLayer.OwnerStack);

        events.Clear();
        scene.Tick(RuntimeTicks.At(1));
        CollectionAssert.AreEqual(
            new[]
            {
                "firstLayer.update",
                "newBehavior.update",
                "addedLayer.update",
            },
            events);
    }

    [TestMethod]
    public void AttachAndDetachHookMutationsAppendAndDrainWithoutRecursiveFlush()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var mutator = layer.Root.Add(new ProbeNode("mutator", events));
        var first = new ProbeNode("first", events);
        var second = new ProbeNode("second", events);
        var replacement = new ProbeNode("replacement", events);
        first.AttachAction = () => first.Add(second);
        first.DetachAction = () => layer.Root.Add(replacement);
        var add = true;
        mutator.UpdateAction = () =>
        {
            if (add)
            {
                add = false;
                layer.Root.Add(first);
            }
            else if (ReferenceEquals(first.Parent, layer.Root))
            {
                layer.Root.Remove(first);
            }
        };
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        Assert.AreSame(layer.Root, first.Parent);
        Assert.AreSame(first, second.Parent);
        Assert.AreSame(layer, second.Layer);
        CollectionAssert.Contains(events, "first.attach");
        CollectionAssert.Contains(events, "second.attach");
        Assert.IsFalse(events.Contains("first.update", StringComparer.Ordinal));
        Assert.IsFalse(events.Contains("second.update", StringComparer.Ordinal));

        events.Clear();
        scene.Tick(RuntimeTicks.At(1));

        Assert.IsNull(first.Parent);
        Assert.IsNull(second.Layer);
        Assert.AreSame(layer.Root, replacement.Parent);
        Assert.AreSame(layer, replacement.Layer);
        Assert.IsTrue(scene.Registry.Contains(replacement));
        Assert.IsFalse(scene.Registry.Contains(first));
        Assert.IsLessThan(events.IndexOf("first.detach"), events.IndexOf("second.detach"));
        Assert.IsLessThan(events.IndexOf("replacement.attach"), events.IndexOf("first.detach"));
    }

    [TestMethod]
    public void AddThenRemoveIsNotCoalescedAndRunsBothLifecycleHooks()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ScreenLayer());
        var mutator = layer.Root.Add(new ProbeNode("mutator", events));
        var transient = new ProbeNode("transient", events);
        var mutate = true;
        mutator.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            layer.Root.Add(transient);
            Assert.IsTrue(layer.Root.Remove(transient));
            Assert.IsNull(transient.Parent);
        };
        scene.Load();
        events.Clear();

        scene.Tick(RuntimeTicks.At(0));

        Assert.IsNull(transient.Parent);
        Assert.IsNull(transient.Layer);
        CollectionAssert.AreEqual(
            new[] { "scene.start", "mutator.update", "transient.attach", "transient.detach" },
            events);
    }

    [TestMethod]
    public void FailedAttachCommitsPrefixRollsBackFailureAndClearsTailReservations()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ScreenLayer());
        var mutator = layer.Root.Add(new ProbeNode("mutator", events));
        var good = new ProbeNode("good", events);
        var failing = new ProbeNode("failing", events)
        {
            AttachAction = () => throw new FlushFailureException("attach"),
        };
        var tail = new ProbeNode("tail", events);
        var mutate = true;
        mutator.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            layer.Root.Add(good);
            layer.Root.Add(failing);
            layer.Root.Add(tail);
        };
        scene.Load();
        events.Clear();

        var exception = Assert.ThrowsExactly<FlushFailureException>(
            () => scene.Tick(RuntimeTicks.At(0)));

        Assert.AreEqual("attach", exception.Message);
        Assert.AreEqual(SceneState.Faulted, scene.State);
        Assert.AreSame(layer.Root, good.Parent);
        Assert.AreSame(layer, good.Layer);
        Assert.IsTrue(scene.Registry.Contains(good));
        Assert.IsNull(failing.Parent);
        Assert.IsNull(failing.Layer);
        Assert.IsNull(tail.Parent);
        Assert.IsNull(failing.ReservationSink);
        Assert.IsNull(tail.ReservationSink);
        new Node2D().Add(tail);
    }

    [TestMethod]
    public void FailedDetachStillCommitsRemovalAndClearsMutationTail()
    {
        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var mutator = layer.Root.Add(new ProbeNode("mutator", []));
        var failing = layer.Root.Add(new ProbeNode("failing", [])
        {
            DetachAction = () => throw new FlushFailureException("detach"),
        });
        var tail = new ProbeNode("tail", []);
        var mutate = true;
        mutator.UpdateAction = () =>
        {
            if (!mutate)
            {
                return;
            }

            mutate = false;
            layer.Root.Remove(failing);
            layer.Root.Add(tail);
        };
        scene.Load();

        var exception = Assert.ThrowsExactly<FlushFailureException>(
            () => scene.Tick(RuntimeTicks.At(0)));

        Assert.AreEqual("detach", exception.Message);
        Assert.IsNull(failing.Parent);
        Assert.IsNull(failing.Layer);
        Assert.IsFalse(scene.Registry.Contains(failing));
        Assert.IsNull(tail.Parent);
        Assert.IsNull(tail.ReservationSink);
        Assert.AreEqual(SceneState.Faulted, scene.State);
    }

    [TestMethod]
    public void ThrowingUpdateAbandonsQueuedTopologyAndReleasesReservations()
    {
        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var mutator = layer.Root.Add(new ProbeNode("mutator", []));
        var pending = new ProbeNode("pending", []);
        mutator.UpdateAction = () =>
        {
            layer.Root.Add(pending);
            throw new FlushFailureException("update");
        };
        scene.Load();

        Assert.ThrowsExactly<FlushFailureException>(() => scene.Tick(RuntimeTicks.At(0)));

        Assert.IsNull(pending.Parent);
        Assert.IsNull(pending.Layer);
        Assert.IsNull(pending.ReservationSink);
        Assert.IsFalse(scene.Registry.Contains(pending));
        Assert.AreEqual(SceneState.Faulted, scene.State);
    }

    [TestMethod]
    public void StartMutationsFlushBeforeUpdateOrRollBackWhenStartThrows()
    {
        var events = new List<string>();
        var successful = new ProbeScene(events);
        var successfulLayer = successful.Layers.Add(new ProbeLayer("layer", events));
        var startedNode = new ProbeNode("startedNode", events);
        successful.StartAction = () => successfulLayer.Root.Add(startedNode);
        successful.Load();
        events.Clear();

        successful.Tick(RuntimeTicks.At(0));

        Assert.AreSame(successfulLayer.Root, startedNode.Parent);
        Assert.IsLessThan(events.IndexOf("layer.update"), events.IndexOf("startedNode.attach"));
        Assert.IsLessThan(events.IndexOf("startedNode.update"), events.IndexOf("layer.update"));

        var failed = new ProbeScene();
        var failedLayer = failed.Layers.Add(new ScreenLayer());
        var pendingNode = new ProbeNode("pending", []);
        var pendingLayer = new ScreenLayer();
        failed.StartAction = () =>
        {
            failedLayer.Root.Add(pendingNode);
            failed.Layers.Add(pendingLayer);
            throw new FlushFailureException("start");
        };
        failed.Load();

        Assert.ThrowsExactly<FlushFailureException>(() => failed.Tick(RuntimeTicks.At(0)));
        Assert.IsNull(pendingNode.Parent);
        Assert.IsNull(pendingNode.ReservationSink);
        Assert.IsNull(pendingLayer.OwnerStack);
        Assert.IsNull(pendingLayer.ReservationStack);
        Assert.AreEqual(SceneState.Faulted, failed.State);
    }

    [TestMethod]
    public void UnloadDiscardsMutationsRequestedByDetachCallbacksWithoutRecursing()
    {
        var scene = new ProbeScene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var replacement = new ProbeNode("replacement", []);
        layer.Root.Add(new ProbeNode("child", [])
        {
            DetachAction = () => layer.Root.Add(replacement),
        });
        scene.Load();

        scene.Unload();

        Assert.IsNull(replacement.Parent);
        Assert.IsNull(replacement.ReservationSink);
        Assert.AreEqual(SceneState.Unloaded, scene.State);
        Assert.AreEqual(0, scene.Registry.Count);
    }

    private sealed class FlushFailureException(string message) : Exception(message);
}
