namespace Najm.Core.Tests.Runtime;

/// <summary>
/// Covers <c>Scene.Own</c>: what releases a native resource, when, in what order, and on the paths
/// where an <c>OnUnload</c> override would never run.
/// </summary>
/// <remarks>
/// The pattern this replaces is a scene-lifetime interop node — a GL texture and the wrap over it —
/// held in a field and disposed from <c>OnUnload</c>. That works and it is what the engine offers if
/// an author prefers it; what it cannot do is survive a load that fails after the resource was
/// acquired, which is the case pinned in the middle of this file.
/// </remarks>
[TestClass]
public sealed class SceneOwnershipTests
{
    [TestMethod]
    public void OwnedResourcesAreReleasedAtUnloadInReverseOrder()
    {
        var log = new List<string>();
        var scene = new OwningScene(log)
        {
            LoadAction = self =>
            {
                self.Acquire("texture");
                self.Acquire("wrap");
            },
        };

        scene.Load(TestEnvironment.Stub());
        scene.Tick(RuntimeTicks.At(0));

        Assert.IsEmpty(log, "nothing is released while the scene is alive");

        scene.Unload();

        CollectionAssert.AreEqual(
            new[] { "scene.unload", "dispose:wrap", "dispose:texture" },
            log,
            "the author's own teardown sees live resources, and the wrap releases before the texture "
            + "whose name it borrowed");
    }

    [TestMethod]
    public void OwnReturnsTheResourceSoItCanWrapTheConstruction()
    {
        var log = new List<string>();
        var scene = new OwningScene(log);
        var resource = new Probe("only", log);

        scene.LoadAction = self => Assert.AreSame(resource, self.Keep(resource));
        scene.Load(TestEnvironment.Stub());
        scene.Unload();

        CollectionAssert.AreEqual(new[] { "scene.unload", "dispose:only" }, log);
    }

    [TestMethod]
    public void AResourceAcquiredBeforeAFailingLoadIsStillReleased()
    {
        // The path an OnUnload override cannot reach: the scene faults inside OnLoad, never
        // completes a load, and therefore never runs OnUnload at all.
        var log = new List<string>();
        var scene = new OwningScene(log)
        {
            LoadAction = self =>
            {
                self.Acquire("texture");
                throw new InvalidOperationException("load failed after acquiring");
            },
        };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => scene.Load(TestEnvironment.Stub()));

        Assert.AreEqual("load failed after acquiring", failure.Message, "the author's failure is the one reported");
        Assert.AreEqual(SceneState.Faulted, scene.State);
        CollectionAssert.AreEqual(new[] { "dispose:texture" }, log, "and the resource did not leak with it");

        scene.Unload();

        CollectionAssert.AreEqual(new[] { "dispose:texture" }, log, "a later unload does not dispose it twice");
    }

    [TestMethod]
    public void OwnedResourcesAreReleasedEvenWhenTheUnloadHookThrows()
    {
        var log = new List<string>();
        var scene = new OwningScene(log)
        {
            LoadAction = self => self.Acquire("texture"),
            UnloadAction = () => throw new InvalidOperationException("unload"),
        };

        scene.Load(TestEnvironment.Stub());

        var failure = Assert.ThrowsExactly<InvalidOperationException>(scene.Unload);

        Assert.AreEqual("unload", failure.Message);
        CollectionAssert.AreEqual(new[] { "scene.unload", "dispose:texture" }, log);
        Assert.AreEqual(SceneState.Unloaded, scene.State);
    }

    [TestMethod]
    public void AFailingDisposeDoesNotStopTheOthersAndIsReported()
    {
        var log = new List<string>();
        var scene = new OwningScene(log)
        {
            LoadAction = self =>
            {
                self.Acquire("first");
                self.Acquire("second", failOnDispose: true);
                self.Acquire("third");
            },
        };

        scene.Load(TestEnvironment.Stub());

        var failure = Assert.ThrowsExactly<InvalidOperationException>(scene.Unload);

        Assert.AreEqual("dispose failed: second", failure.Message);
        CollectionAssert.AreEqual(
            new[] { "scene.unload", "dispose:third", "dispose:second", "dispose:first" },
            log,
            "the failure is collected, not thrown out of the loop");
    }

    [TestMethod]
    public void OwningIsRefusedOnceTheSceneHasTornDown()
    {
        var log = new List<string>();
        var scene = new OwningScene(log);

        scene.Load(TestEnvironment.Stub());
        scene.Unload();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => scene.Acquire("late"),
            "nothing registered after teardown would ever be released");
        Assert.IsEmpty(log.Where(entry => entry.StartsWith("dispose:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void OwnRejectsNull()
    {
        var scene = new OwningScene([]);

        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Keep<Probe>(null!));
    }

    [TestMethod]
    public void ADetachIsNotADisposal()
    {
        // The reason Own is a scene member and not a node one: OnDetach cannot tell a removal from
        // a re-parent, so a node that released its resource there would destroy it mid-move.
        var log = new List<string>();
        var scene = new OwningScene(log);
        var layer = scene.Layers.Add(new ScreenLayer());
        var moving = new CountingNode();
        var group = layer.Root.Add(new Node2D());
        group.Add(moving);

        scene.Load(TestEnvironment.Stub());
        scene.Tick(RuntimeTicks.At(0));

        Assert.AreEqual(1, moving.Attaches);
        Assert.AreEqual(0, moving.Detaches);

        Assert.IsTrue(group.Remove(moving));
        layer.Root.Add(moving);
        scene.Tick(RuntimeTicks.At(1));

        Assert.AreEqual(1, moving.Detaches, "a re-parent detaches the node");
        Assert.AreEqual(2, moving.Attaches, "and attaches it again, live, in the same scene");
    }

    /// <summary>A disposable that records what happened to it and can refuse to go quietly.</summary>
    private sealed class Probe : IDisposable
    {
        private readonly List<string> log;
        private readonly string name;
        private readonly bool failOnDispose;

        internal Probe(string name, List<string> log, bool failOnDispose = false)
        {
            this.name = name;
            this.log = log;
            this.failOnDispose = failOnDispose;
        }

        public void Dispose()
        {
            log.Add($"dispose:{name}");
            if (failOnDispose)
            {
                throw new InvalidOperationException($"dispose failed: {name}");
            }
        }
    }

    /// <summary>A scene whose hooks the test fills in, exposing <c>Own</c> to it.</summary>
    private sealed class OwningScene : Scene
    {
        private readonly List<string> log;

        internal OwningScene(List<string> log) => this.log = log;

        internal Action<OwningScene>? LoadAction { get; set; }

        internal Action? UnloadAction { get; set; }

        /// <summary>Registers a fresh probe under this name.</summary>
        internal Probe Acquire(string name, bool failOnDispose = false) =>
            Own(new Probe(name, log, failOnDispose));

        /// <summary>Registers an existing resource, and hands back what <c>Own</c> returned.</summary>
        internal T Keep<T>(T resource)
            where T : class, IDisposable => Own(resource);

        protected override void OnLoad() => LoadAction?.Invoke(this);

        protected override void OnUnload()
        {
            log.Add("scene.unload");
            UnloadAction?.Invoke();
        }
    }

    /// <summary>A node that counts its attachment transitions.</summary>
    private sealed class CountingNode : Node2D
    {
        internal int Attaches { get; private set; }

        internal int Detaches { get; private set; }

        protected override void OnAttach() => Attaches++;

        protected override void OnDetach() => Detaches++;
    }
}
