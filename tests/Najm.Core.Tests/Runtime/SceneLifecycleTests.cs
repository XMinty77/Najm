namespace Najm.Core.Tests.Runtime;

[TestClass]
public sealed class SceneLifecycleTests
{
    [TestMethod]
    public void LoadAndFirstTickFollowEngineControlledOrder()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events);
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var node = layer.Root.Add(new ProbeNode("node", events));
        node.Behaviors.Add(new ProbeBehavior("behavior", events));

        scene.Load();
        scene.Tick(RuntimeTicks.At(0));
        scene.Tick(RuntimeTicks.At(1));

        CollectionAssert.AreEqual(
            new[]
            {
                "layer.attach",
                "node.attach",
                "behavior.attach",
                "scene.load",
                "scene.start",
                "layer.update",
                "node.update",
                "behavior.update",
                "layer.update",
                "node.update",
                "behavior.update",
            },
            events);
        Assert.AreEqual(SceneState.Started, scene.State);
        Assert.AreEqual(2, scene.Registry.Count);
    }

    [TestMethod]
    public void InvalidTransitionsFailWithoutConsumingHooks()
    {
        var scene = new ProbeScene();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Tick(RuntimeTicks.At(0)));
        Assert.ThrowsExactly<InvalidOperationException>(scene.Stop);
        Assert.ThrowsExactly<InvalidOperationException>(scene.Unload);
        Assert.IsEmpty(scene.Events);

        scene.Load();
        Assert.ThrowsExactly<InvalidOperationException>(scene.Load);
        scene.Tick(RuntimeTicks.At(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Tick(RuntimeTicks.At(0)));
        scene.Stop();
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Tick(RuntimeTicks.At(1)));
        scene.Unload();
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Tick(RuntimeTicks.At(2)));
        Assert.ThrowsExactly<InvalidOperationException>(scene.Load);
    }

    [TestMethod]
    public void StopAndUnloadAreIdempotentAndPairOnlyCompletedHooks()
    {
        var started = new ProbeScene();
        started.Load();
        started.Tick(RuntimeTicks.At(0));

        started.Stop();
        started.Stop();
        started.Unload();
        started.Unload();

        CollectionAssert.AreEqual(
            new[] { "scene.load", "scene.start", "scene.stop", "scene.unload" },
            started.Events);
        Assert.AreEqual(SceneState.Unloaded, started.State);

        var neverStarted = new ProbeScene();
        neverStarted.Load();
        neverStarted.Stop();
        neverStarted.Unload();

        CollectionAssert.AreEqual(new[] { "scene.load", "scene.unload" }, neverStarted.Events);
    }

    [TestMethod]
    public void FailedStartIsTerminalAndIsNotPairedWithStop()
    {
        var scene = new ProbeScene
        {
            StartAction = () => throw new TestLifecycleException("start"),
        };
        scene.Load();

        var exception = Assert.ThrowsExactly<TestLifecycleException>(
            () => scene.Tick(RuntimeTicks.At(0)));

        Assert.AreEqual("start", exception.Message);
        Assert.AreEqual(SceneState.Faulted, scene.State);
        scene.Stop();
        scene.Unload();
        CollectionAssert.AreEqual(
            new[] { "scene.load", "scene.start", "scene.unload" },
            scene.Events);
    }

    [TestMethod]
    public void FailedLoadRollsBackAttachmentAndIsNotPairedWithUnload()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events)
        {
            LoadAction = () => throw new TestLifecycleException("load"),
        };
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var node = layer.Root.Add(new ProbeNode("node", events));

        var exception = Assert.ThrowsExactly<TestLifecycleException>(scene.Load);

        Assert.AreEqual("load", exception.Message);
        Assert.AreEqual(SceneState.Faulted, scene.State);
        Assert.IsNull(layer.AttachedScene);
        Assert.IsNull(layer.Root.Layer);
        Assert.IsNull(node.Layer);
        Assert.AreEqual(0, scene.Registry.Count);

        scene.Unload();
        CollectionAssert.AreEqual(
            new[] { "layer.attach", "node.attach", "scene.load", "node.detach", "layer.detach" },
            events);
    }

    [TestMethod]
    public void FailedLoadDoesNotStealSnapshotLayerClaimedByAnotherScene()
    {
        var firstScene = new ProbeScene();
        var secondScene = new ProbeScene();
        var snapshotLayer = firstScene.Layers.Add(new ScreenLayer());
        var transientLayer = new ScreenLayer();
        firstScene.LoadAction = () =>
        {
            Assert.IsTrue(firstScene.Layers.Remove(snapshotLayer));
            secondScene.Layers.Add(snapshotLayer);
            firstScene.Layers.Add(transientLayer);
            throw new TestLifecycleException("load");
        };

        var aggregate = Assert.ThrowsExactly<AggregateException>(firstScene.Load);

        Assert.HasCount(2, aggregate.InnerExceptions);
        Assert.IsInstanceOfType<TestLifecycleException>(aggregate.InnerExceptions[0]);
        StringAssert.Contains(aggregate.InnerExceptions[1].Message, "another scene claimed the layer");
        Assert.AreEqual(SceneState.Faulted, firstScene.State);
        Assert.IsEmpty(firstScene.Layers);
        Assert.IsNull(transientLayer.OwnerStack);
        Assert.IsNull(transientLayer.AttachedScene);
        Assert.HasCount(1, secondScene.Layers);
        Assert.AreSame(snapshotLayer, secondScene.Layers[0]);
        Assert.AreSame(secondScene.Layers, snapshotLayer.OwnerStack);
        Assert.IsNull(snapshotLayer.AttachedScene);

        Assert.IsTrue(secondScene.Layers.Remove(snapshotLayer));
        Assert.IsNull(snapshotLayer.OwnerStack);
        secondScene.Layers.Add(snapshotLayer);
        Assert.AreSame(secondScene.Layers, snapshotLayer.OwnerStack);
    }

    [TestMethod]
    public void StopAndUnloadFailuresStillCompleteEngineCleanup()
    {
        var events = new List<string>();
        var scene = new ProbeScene(events)
        {
            StopAction = () => throw new TestLifecycleException("stop"),
            UnloadAction = () => throw new TestLifecycleException("unload"),
        };
        var layer = scene.Layers.Add(new ProbeLayer("layer", events));
        var child = layer.Root.Add(new ProbeNode("child", events)
        {
            DetachAction = () => throw new TestLifecycleException("detach"),
        });
        scene.Load();
        scene.Tick(RuntimeTicks.At(0));

        Assert.ThrowsExactly<TestLifecycleException>(scene.Stop);
        var aggregate = Assert.ThrowsExactly<AggregateException>(scene.Unload);

        Assert.HasCount(2, aggregate.InnerExceptions);
        Assert.AreEqual(SceneState.Unloaded, scene.State);
        Assert.AreEqual(0, scene.Registry.Count);
        Assert.IsNull(layer.AttachedScene);
        Assert.IsNull(child.Layer);
        scene.Stop();
        scene.Unload();
        Assert.AreEqual(1, events.Count(item => item == "scene.stop"));
        Assert.AreEqual(1, events.Count(item => item == "scene.unload"));
    }

    private sealed class TestLifecycleException(string message) : Exception(message);
}
