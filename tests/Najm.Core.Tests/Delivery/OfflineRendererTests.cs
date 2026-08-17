using System.Numerics;

namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The normative offline timing contract, in the loop that has to honor it: tick <c>k</c> carries
/// <c>Dt = 1/fps</c> and <c>Elapsed = (k+1)/fps</c>, and output frame <c>k</c> is the render after
/// tick <c>k</c>. Every expectation below is derived from that arithmetic, never captured.
/// </summary>
[TestClass]
public sealed class OfflineRendererTests
{
    [TestMethod]
    public void HalfASecondAtSixtyIsExactlyThirtyTicksAndThirtyFrames()
    {
        // 0.5 × 60 = 30 exactly. The frame count is the tick count, because a frame is a render
        // after a tick — not 29 (dropping the boundary) and not 31 (rendering the loaded state too).
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d, Duration = 0.5d });

        Assert.AreEqual(30L, frames);
        Assert.HasCount(30, scene.Elapsed, "0.5 s at 60 fps is exactly 30 ticks.");
        Assert.HasCount(30, sink.Frames);
        Assert.AreEqual(1, sink.BeginCount);
        Assert.AreEqual(1, sink.EndCount);
        Assert.AreEqual(30L, sink.Info.FrameCount);
    }

    [TestMethod]
    public void TickKCarriesTheDerivedFixedStepTime()
    {
        const double Fps = 60d;
        const int Frames = 90;

        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = Fps, Frames = Frames });

        Assert.HasCount(Frames, scene.Elapsed);
        for (var k = 0; k < Frames; k++)
        {
            Assert.AreEqual((long)k, scene.Frames[k], "Frame indices start at zero and increase by one.");
            Assert.AreEqual(1d / Fps, scene.Deltas[k], 1e-12d, $"Tick {k} must carry Dt = 1/fps.");
            Assert.AreEqual(
                (k + 1d) / Fps,
                scene.Elapsed[k],
                1e-12d,
                $"Tick {k} must carry Elapsed = (k+1)/fps, derived rather than accumulated.");

            // Output frame k is the render performed after tick k, so the indices coincide.
            Assert.AreEqual((long)k, sink.Frames[k]);
        }

        // The last tick lands exactly on the run's duration: 90/60 = 1.5 s.
        Assert.AreEqual(1.5d, scene.Elapsed[^1], 1e-12d);
    }

    [TestMethod]
    public void AStillAtZeroRunsZeroTicksAndDoesNotStartTheScene()
    {
        // PLAN resolution 1: at: 0 renders the loaded state. OnStart runs inside the first tick, so
        // a zero-tick export must not run it.
        var scene = new CountingScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var ticks = OfflineRenderer.RenderStill(scene, surfaces, sink, at: 0d, framesPerSecond: 60d);

        Assert.AreEqual(0L, ticks, "at: 0 is ceil(0 × fps) = 0 ticks.");
        Assert.AreEqual(0L, scene.UpdateCount, "A zero-tick export must not update anything.");
        Assert.AreEqual(0, scene.StartCount, "OnStart runs inside the first tick, and there was none.");
        Assert.HasCount(1, sink.Frames, "The loaded state is still rendered and delivered.");
        Assert.AreEqual(0L, sink.Frames[0], "A still is a one-frame stream indexed from zero.");
        Assert.AreEqual(1L, sink.Info.FrameCount);
        Assert.AreEqual(SceneState.Unloaded, scene.State);
    }

    [TestMethod]
    public void AStillAtHalfASecondRunsThirtyTicksAndDoesStartTheScene()
    {
        var scene = new CountingScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var ticks = OfflineRenderer.RenderStill(scene, surfaces, sink, at: 0.5d, framesPerSecond: 60d);

        Assert.AreEqual(30L, ticks, "ceil(0.5 × 60) = 30.");
        Assert.AreEqual(30L, scene.UpdateCount);
        Assert.AreEqual(1, scene.StartCount, "OnStart runs exactly once, inside the first tick.");
        Assert.HasCount(1, sink.Frames, "A still renders once after seeking, not once per tick.");
        Assert.AreEqual(SceneState.Unloaded, scene.State);
    }

    [TestMethod]
    public void AStillRoundsAnyPartialFrameUpToAWholeTick()
    {
        // ceil semantics: the smallest positive time still needs one tick, and a time a hair past a
        // frame boundary needs the next one.
        Assert.AreEqual(1L, TicksFor(at: 1d / 600d, fps: 60d), "A tenth of a frame still needs a tick.");
        Assert.AreEqual(30L, TicksFor(at: 0.5d, fps: 60d), "An exact boundary keeps its own tick.");
        Assert.AreEqual(31L, TicksFor(at: 0.5001d, fps: 60d), "Anything past the boundary advances.");
        Assert.AreEqual(0L, TicksFor(at: 0d, fps: 60d));
    }

    [TestMethod]
    public void AnExplicitFrameCountWinsOverADuration()
    {
        // Documented precedence: the reference leaves the combination unspecified, and Najm resolves
        // it toward the exact count. A duration of 2 s at 60 fps would be 120 frames.
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var options = new OfflineOptions
        {
            Sink = sink,
            Fps = 60d,
            Duration = 2d,
            Frames = 7L,
        };

        Assert.AreEqual(7L, options.ResolveFrameCount());

        var frames = OfflineRenderer.Render(scene, surfaces, options);

        Assert.AreEqual(7L, frames);
        Assert.HasCount(7, scene.Elapsed);
        Assert.HasCount(7, sink.Frames);
    }

    [TestMethod]
    public void ADurationAloneBecomesCeilingOfDurationTimesFps()
    {
        Assert.AreEqual(
            120L,
            new OfflineOptions { Sink = new RecordingFrameSink(), Fps = 60d, Duration = 2d }
                .ResolveFrameCount());
        Assert.AreEqual(
            121L,
            new OfflineOptions { Sink = new RecordingFrameSink(), Fps = 60d, Duration = 2.001d }
                .ResolveFrameCount());
        Assert.AreEqual(
            0L,
            new OfflineOptions { Sink = new RecordingFrameSink(), Fps = 60d, Duration = 0d }
                .ResolveFrameCount());
    }

    [TestMethod]
    public void ALengthlessConfigurationFailsBeforeTheSceneIsTouched()
    {
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(scene, surfaces, new OfflineOptions { Sink = new RecordingFrameSink() }));

        Assert.AreEqual(SceneState.Constructed, scene.State);
        Assert.AreEqual(0, scene.LoadCount);
    }

    [TestMethod]
    public void ASinkFailurePropagatesAndLeavesNoLoadedScene()
    {
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new FailingFrameSink(failOnFrame: 3L);

        var failure = Assert.ThrowsExactly<IOException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = 60d, Frames = 40L }));

        Assert.IsTrue(
            failure.Message.Contains("frame 3", StringComparison.Ordinal),
            $"The sink's own failure must reach the caller intact, but the message was '{failure.Message}'.");
        Assert.AreEqual(4, sink.SubmittedFrames, "The loop stops at the failing frame.");
        Assert.AreEqual(0, sink.EndCount, "An abandoned stream is never reported as finished.");
        Assert.AreEqual(1, sink.DisposeCount, "A disposable sink is released so no encoder outlives the run.");
        Assert.AreEqual(SceneState.Unloaded, scene.State, "The scene must not be left loaded.");
        Assert.AreEqual(1, scene.UnloadCount);
        Assert.IsTrue(surfaces.LastTarget!.Disposed, "The output target is released too.");
        Assert.IsTrue(surfaces.LastCompositor!.Disposed);
    }

    [TestMethod]
    public void AFaultingSceneAlsoUnloadsAndReleasesTheSink()
    {
        var scene = new FaultingScene(faultOnFrame: 2L);
        using var surfaces = new StubSurfaceProvider();
        var sink = new FailingFrameSink(failOnFrame: -1L);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = 60d, Frames = 10L }));

        Assert.AreEqual(SceneState.Unloaded, scene.State);
        Assert.AreEqual(1, sink.DisposeCount);
        Assert.AreEqual(0, sink.EndCount);
        Assert.AreEqual(2, sink.SubmittedFrames, "Frames 0 and 1 got through before frame 2 faulted.");
    }

    [TestMethod]
    public void TheStreamDescriptionMatchesTheRequestedOutput()
    {
        var scene = new TimelineScene { VirtualResolution = new Vector2(1920f, 1080f) };
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions
            {
                Sink = sink,
                Fps = 30d,
                Frames = 2L,
                Scale = 0.25f,
                Format = PixelFormat.Rgba8888,
            });

        // 1920 × 0.25 = 480 and 1080 × 0.25 = 270, exactly.
        Assert.AreEqual(480, sink.Info.Width);
        Assert.AreEqual(270, sink.Info.Height);
        Assert.AreEqual(30d, sink.Info.FramesPerSecond);
        Assert.AreEqual(PixelFormat.Rgba8888, sink.Info.Format);
        Assert.AreEqual(2L, sink.Info.FrameCount);
        Assert.AreEqual(1920, sink.Info.RowBytes);
        Assert.AreEqual(new PixelSize(480, 270), surfaces.LastTarget!.Size);
    }

    [TestMethod]
    public void AnExplicitOutputSizeWinsOverTheScale()
    {
        var scene = new TimelineScene { VirtualResolution = new Vector2(1920f, 1080f) };
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions
            {
                Sink = sink,
                Frames = 1L,
                Scale = 4f,
                OutputSize = new PixelSize(320, 240),
            });

        Assert.AreEqual(320, sink.Info.Width);
        Assert.AreEqual(240, sink.Info.Height);
    }

    [TestMethod]
    public void EachSubmittedFrameCarriesItsOwnPixels()
    {
        // The stub compositor stamps the frame ordinal into the surface, so identical bytes across
        // frames would mean the loop reused a stale capture.
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        OfflineRenderer.Render(scene, surfaces, new OfflineOptions { Sink = sink, Frames = 5L });

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, sink.FirstBytes);
        Assert.AreEqual(5, surfaces.LastCompositor!.RenderCount);
        Assert.AreEqual(5, surfaces.LastTarget!.SnapshotCount);
    }

    [TestMethod]
    public void AZeroLengthRunOpensAndClosesAnEmptyStream()
    {
        var scene = new TimelineScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(scene, surfaces, new OfflineOptions { Sink = sink, Frames = 0L });

        Assert.AreEqual(0L, frames);
        Assert.AreEqual(1, sink.BeginCount);
        Assert.AreEqual(1, sink.EndCount);
        Assert.IsEmpty(sink.Frames);
        Assert.IsEmpty(scene.Elapsed);
        Assert.AreEqual(SceneState.Unloaded, scene.State);
    }

    [TestMethod]
    public void AWarmOfflineLoopAllocatesNoManagedBytesPerFrame()
    {
        const long WarmFrames = 64L;
        const long TotalFrames = 1_064L;

        var scene = new CountingScene { VirtualResolution = new Vector2(64f, 32f) };
        using var surfaces = new StubSurfaceProvider();
        var sink = new AllocationProbeSink(WarmFrames, TotalFrames);

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d, Frames = TotalFrames });

        Assert.AreEqual(TotalFrames - WarmFrames, sink.MeasuredFrames);
        Assert.AreEqual(
            0L,
            sink.AllocatedBytes,
            $"The warm offline loop allocated {sink.AllocatedBytes} managed bytes over " +
            $"{sink.MeasuredFrames} frames. Pixel leases are pooled precisely so this stays zero.");
    }

    private static long TicksFor(double at, double fps)
    {
        var scene = new CountingScene();
        using var surfaces = new StubSurfaceProvider();
        return OfflineRenderer.RenderStill(scene, surfaces, new RecordingFrameSink(), at, fps);
    }

    /// <summary>A scene that throws out of a chosen tick, the way faulting author code would.</summary>
    private sealed class FaultingScene : Scene
    {
        internal FaultingScene(long faultOnFrame) => Layers.Add(new FaultingLayer(faultOnFrame));

        private sealed class FaultingLayer : ScreenLayer
        {
            private readonly long faultOnFrame;

            internal FaultingLayer(long faultOnFrame) => this.faultOnFrame = faultOnFrame;

            protected override void Update(in TickContext tick)
            {
                if (tick.Time.Frame == faultOnFrame)
                {
                    throw new InvalidOperationException($"Author code faulted on frame {faultOnFrame}.");
                }
            }
        }
    }
}
