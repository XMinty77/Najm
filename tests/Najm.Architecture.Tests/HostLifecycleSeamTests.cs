using System.Numerics;
using Najm.Core;
using Najm.Skia;

namespace Najm.Architecture.Tests;

/// <summary>
/// Proves that a driver <em>outside</em> `Najm.Core` can run a scene end to end, which
/// ARCHITECTURE §4.6 and §4.7 require of every host and §16 puts in its own assembly.
/// </summary>
/// <remarks>
/// This assembly is deliberately not on `Najm.Core`'s `InternalsVisibleTo` list, so it compiles
/// against exactly the surface a third-party host has. If `Load`, `Stop`, or `Unload` were to
/// become internal again, this file would stop compiling — which is the point of putting the check
/// here rather than beside the other lifecycle tests.
/// </remarks>
[TestClass]
public sealed class HostLifecycleSeamTests
{
    [TestMethod]
    public void AHostOutsideCoreCanAssembleTickRenderAndTearDownAScene()
    {
        // §4.7's loop, minus the window: assemble capabilities, load, advance a clock, tick, render,
        // stop, unload. Every call below is public API.
        using var surfaces = new RasterSkiaSurfaceProvider();
        var scene = new HostDrivenScene();

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        try
        {
            Assert.IsNotNull(scene.Env);
            Assert.AreEqual(new Vector2(320f, 180f), scene.VirtualResolution);

            using var target = surfaces.CreateTarget(new SurfaceSpec(320, 180, 1, ColorSpace.Srgb));
            var clock = new FrameClock(ClockPolicy.Live(maxDt: 0.1));
            var input = new InputBuffer();
            input.Reserve(Key.F1);

            for (var frame = 0; frame < 3; frame++)
            {
                input.BeginFrame();
                input.MovePointer(0, new Vector2(160f + frame, 90f));
                input.PressPointer(0, new Vector2(160f + frame, 90f), PointerButton.Left);

                var time = clock.Advance(1d / 60d);
                scene.Tick(new TickContext(time, input.Block));
                scene.Render(target);
            }

            Assert.AreEqual(3, scene.Ticks);
            Assert.AreEqual(3, scene.Presses, "The router reached the node through a host-fed block.");
            Assert.IsTrue(scene.Started);
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }

        Assert.IsTrue(scene.Stopped);
        Assert.IsTrue(scene.Unloaded);
    }

    [TestMethod]
    public void TheStateMachineStillRefusesOutOfOrderCommandsFromOutside()
    {
        // §4.1's promise is kept by the transitions, not by visibility: a driver that can call
        // these still cannot call them wrongly.
        using var surfaces = new RasterSkiaSurfaceProvider();
        var scene = new HostDrivenScene();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => scene.Tick(new TickContext(new TimeInfo(1d, 1d, 0L, isFixedStep: false))));
        Assert.ThrowsExactly<InvalidOperationException>(scene.Stop);
        Assert.ThrowsExactly<InvalidOperationException>(scene.Unload);

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps)));

        scene.Tick(new TickContext(new TimeInfo(1d, 1d, 0L, isFixedStep: false)));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => scene.Tick(new TickContext(new TimeInfo(1d, 1d, 0L, isFixedStep: false))),
            "Frame indices must advance strictly.");

        // Both are idempotent, and Unload runs Stop for a driver that skipped it.
        scene.Unload();
        scene.Unload();
        scene.Stop();
        Assert.IsTrue(scene.Stopped);
    }

    private sealed class HostDrivenScene : Scene
    {
        public HostDrivenScene()
        {
            VirtualResolution = new Vector2(320f, 180f);
            Layer = new ScreenLayer();
            Layers.Add(Layer);
        }

        internal ScreenLayer Layer { get; }

        internal int Ticks { get; private set; }

        internal int Presses { get; private set; }

        internal bool Started { get; private set; }

        internal bool Stopped { get; private set; }

        internal bool Unloaded { get; private set; }

        protected override void OnLoad() => Layer.Root.Add(new WholeFrameTarget(() => Presses++));

        protected override void OnStart() => Started = true;

        protected override void Update(in TickContext tick) => Ticks++;

        protected override void OnStop() => Stopped = true;

        protected override void OnUnload() => Unloaded = true;
    }

    /// <summary>A node covering the frame that counts the presses the router delivers to it.</summary>
    private sealed class WholeFrameTarget(Action onPress) : Node2D, IInteractive
    {
        public override Rect HitBounds => new(0f, 0f, 320f, 180f);

        public bool OnPointerDown(in PointerArgs args)
        {
            onPress();
            return true;
        }
    }
}
