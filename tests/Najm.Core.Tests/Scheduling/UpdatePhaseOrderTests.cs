using System.Numerics;
using Najm.Core.Tests.Delivery;

namespace Najm.Core.Tests.Scheduling;

/// <summary>Covers the shape of the Update phase and the phases scheduling is illegal from.</summary>
[TestClass]
public sealed class UpdatePhaseOrderTests
{
    [TestMethod]
    public void TheUpdatePhaseRunsSceneThenTheTreeThenTweensThenCoroutines()
    {
        var scene = new SchedulerScene();
        var layer = scene.Layers.Add(new LoggingLayer("layer", scene.Events));
        layer.Root.Add(new LoggingNode("node", scene.Events));

        IEnumerator<Wait> Routine()
        {
            while (true)
            {
                scene.Events.Add("routine");
                yield return Wait.NextFrame;
            }
        }

        var tweenWrites = 0;
        scene.StartAction = s =>
        {
            s.Animate(
                _ =>
                {
                    tweenWrites++;
                    if (tweenWrites > 1)
                    {
                        // The first write is the from-value applied here in OnStart, which is not a
                        // pass event and does not belong in the per-tick ordering.
                        s.Events.Add("tween");
                    }
                },
                from: 0f,
                to: 1f,
                duration: 10d);
            s.Start(Routine());
        };

        scene.Load(TestEnvironment.Stub());
        scene.Tick(SchedulerTicks.At(0));

        CollectionAssert.AreEqual(
            new[] { "scene.start", "scene.update", "layer.update", "node.update", "tween", "routine" },
            scene.Events,
            "Update is: the scene hook, the layer traversal, the tween pass, then the coroutine pass");

        scene.Events.Clear();
        scene.Tick(SchedulerTicks.At(1));

        CollectionAssert.AreEqual(
            new[] { "scene.update", "layer.update", "node.update", "tween", "routine" },
            scene.Events,
            "and every later tick repeats it");
    }

    [TestMethod]
    public void SceneUpdateRunsExactlyOncePerTick()
    {
        var harness = new SchedulerHarness().Load();

        Assert.AreEqual(0, harness.Scene.UpdateCount, "loading is not a tick");

        harness.Tick(7);

        Assert.AreEqual(7, harness.Scene.UpdateCount);
    }

    [TestMethod]
    public void SceneUpdateRunsWithNoLayersAtAll()
    {
        var scene = new SchedulerScene();
        scene.Load(TestEnvironment.Stub());
        scene.Tick(SchedulerTicks.At(0));

        Assert.AreEqual(1, scene.UpdateCount);
    }

    [TestMethod]
    public void SceneUpdateSeesTheFramesTimeAndMayScheduleFromIt()
    {
        var scene = new SchedulerScene();
        var resumed = false;

        IEnumerator<Wait> Routine()
        {
            resumed = true;
            yield break;
        }

        var started = false;
        scene.UpdateAction = s =>
        {
            if (started)
            {
                return;
            }

            started = true;
            s.Start(Routine());
        };

        scene.Load(TestEnvironment.Stub());
        scene.Tick(SchedulerTicks.At(0));

        Assert.IsTrue(resumed, "a routine started in the scene hook is queued before this frame's pass");
    }

#if DEBUG
    [TestMethod]
    public void StartingFromRenderIsRefusedInDebugBuilds()
    {
        // Debug-only by specification (§10.2.6). Release builds compile the guard away, so this
        // test exists only in a Debug run. The Layout phase is the other illegal caller and has no
        // check yet because the phase itself does not exist.
        var scene = new SchedulerScene();
        var provider = new CallbackSurfaceProvider();
        var target = new StubRenderTarget(new SurfaceSpec(64, 64));

        IEnumerator<Wait> Routine()
        {
            yield break;
        }

        scene.Load(new SceneEnvironment(provider));

        provider.Compositor.OnRender = () => scene.Start(Routine());
        var routineFailure = Assert.ThrowsExactly<InvalidOperationException>(() => scene.Render(target));
        StringAssert.Contains(routineFailure.Message, "render idempotence");

        provider.Compositor.OnRender = () => scene.Animate(_ => { }, 0f, 1f, 1d);
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Render(target));
    }

    private sealed class CallbackSurfaceProvider : ISurfaceProvider
    {
        internal CallbackCompositor Compositor { get; } = new();

        public RenderCaps Caps => RenderCaps.None;

        public IRenderTarget CreateTarget(in SurfaceSpec spec) => new StubRenderTarget(spec);

        public ICompositor CreateCompositor() => Compositor;

        public void Dispose()
        {
        }
    }

    private sealed class CallbackCompositor : ICompositor
    {
        public CompositorStats Stats => default;

        public CompositorDebugOptions Debug { get; } = new();

        internal Action? OnRender { get; set; }

        public void Render(
            LayerStack layers,
            IRenderTarget output,
            in Vector2 virtualResolution,
            float renderScale) =>
            OnRender?.Invoke();

        public void Dispose()
        {
        }
    }
#endif
}
