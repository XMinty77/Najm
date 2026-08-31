namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The offline run whose length is the scene's own choreography: neither <c>Frames</c> nor
/// <c>Duration</c> set.
/// </summary>
/// <remarks>
/// The case this exists for is a scene that publishes its length by hand, by summing the constants
/// its beats are written in, and gets it wrong — because waits add whole frames no constant can see.
/// <see cref="OverrunScene"/> is that scene in miniature: two beats of 0.25 s, so a hand-summed
/// 0.5 s and exactly 30 frames, against a routine whose last statement runs in frame 31. Both
/// numbers are pinned below, because a test that only checked "the clip contains the last beat"
/// would pass under the arithmetic that truncates it. The beats are quarter-seconds so that the sum
/// is exact in binary and the two frames lost are the scheduler's doing and nothing else's.
/// </remarks>
[TestClass]
public sealed class OpenEndedOfflineTests
{
    [TestMethod]
    public void TheHandSummedDurationCutsTheClipBeforeTheRoutineFinishes()
    {
        // This is the defect, reproduced: the number the scene can compute about itself is 30
        // frames, and the choreography is not over until frame 31.
        var scene = new OverrunScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d, Duration = OverrunScene.HandSummedSeconds });

        Assert.AreEqual(30L, frames, "0.5 s at 60 fps is 30 frames, which is what the beat constants sum to");
        Assert.AreEqual(
            -1L,
            scene.FinishedAt,
            "and the routine had not reached its last statement when the clip was cut");
    }

    [TestMethod]
    public void AnOpenEndedRunEndsOnTheFrameTheLastRoutineFinishedOn()
    {
        var scene = new OverrunScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d });

        // 15 ticks for the first beat and 15 for the second — 30, as the constants say — plus one
        // for the rejoin from the helper routine, and one because the last frame is rendered rather
        // than merely reached. Neither is visible to a sum of beat constants.
        Assert.AreEqual(31L, scene.FinishedAt, "the routine's last statement runs in frame 31's pass");
        Assert.AreEqual(32L, frames, "so the clip is 32 frames: frame 31 is rendered and then the run ends");
        Assert.HasCount(32, sink.Frames);
        Assert.AreEqual(31L, sink.Frames[^1]);
        Assert.IsNull(sink.Info.FrameCount, "the length is not known when the stream is begun");
        Assert.AreEqual(1, sink.EndCount, "and the stream is still finished properly");
    }

    [TestMethod]
    public void ALiveTweenKeepsTheRunGoingWithNoRoutineAtAll()
    {
        // "Until the coroutines finish" is not enough on its own: a scene whose last motion is a
        // tween is not finished while the tween is still writing values.
        var scene = new TweenOnlyScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d });

        // The tween is created in OnStart, before frame 0's tween pass, so it consumes deltas on
        // frames 0 through 14: fifteen of them, which is 0.25 s.
        Assert.AreEqual(15L, frames);
        Assert.AreEqual(1d, scene.Value, "and the last frame rendered is the one the tween landed on");
    }

    [TestMethod]
    public void ASceneThatSchedulesNothingIsOneFrameLong()
    {
        var scene = new IdleScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(scene, surfaces, new OfflineOptions { Sink = sink });

        Assert.AreEqual(1L, frames, "the first tick is always run: OnStart, which schedules, is inside it");
        Assert.AreEqual(1, scene.StartCount);
    }

    [TestMethod]
    public void WorkThatNeverFinishesStopsAtTheCeilingWithAnException()
    {
        var scene = new NeverEndingScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new FailingFrameSink(failOnFrame: -1L);

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = 60d, MaxFrames = 10L }));

        Assert.IsTrue(
            failure.Message.Contains("MaxFrames", StringComparison.Ordinal),
            $"the message must name the knob that raises the ceiling; it was '{failure.Message}'");
        Assert.AreEqual(10, sink.SubmittedFrames, "the ceiling is a frame count, and it is exact");
        Assert.AreEqual(0, sink.EndCount, "a run that hit the ceiling is not a finished stream");
        Assert.AreEqual(1, sink.DisposeCount);
        Assert.AreEqual(SceneState.Unloaded, scene.State, "and the scene is unloaded on the way out");
    }

    [TestMethod]
    public void APausedRoutineIsUnfinishedWorkRatherThanFinishedWork()
    {
        // Paused is not terminal. A run whose only routine is paused has nothing to wait for and
        // waits anyway, which is the honest reading of "has not finished" and is why the ceiling
        // exists.
        var scene = new PausedScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new FailingFrameSink(failOnFrame: -1L);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = 60d, MaxFrames = 4L }));

        Assert.AreEqual(4, sink.SubmittedFrames);
    }

    [TestMethod]
    public void TheCeilingIsConsultedOnlyByARunWithNoStatedLength()
    {
        var scene = new NeverEndingScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var frames = OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = sink, Fps = 60d, Frames = 5L, MaxFrames = 1L });

        Assert.AreEqual(5L, frames, "a stated length is its own bound; MaxFrames does not shorten it");
    }

    [TestMethod]
    public void TheDefaultCeilingIsAnHourOfSimulatedTimeAtTheRunsRate()
    {
        var sink = new RecordingFrameSink();

        Assert.AreEqual(
            216_000L,
            new OfflineOptions { Sink = sink, Fps = 60d }.ResolveFrameLimit(),
            "3600 s at 60 fps");
        Assert.AreEqual(
            86_400L,
            new OfflineOptions { Sink = sink, Fps = 24d }.ResolveFrameLimit(),
            "the ceiling is a duration, so it tracks the rate");
        Assert.AreEqual(
            7L,
            new OfflineOptions { Sink = sink, Fps = 24d, MaxFrames = 7L }.ResolveFrameLimit());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new OfflineOptions { Sink = sink, MaxFrames = 0L });
    }

    [TestMethod]
    public void AStatedLengthIsStillNotOpenEnded()
    {
        var sink = new RecordingFrameSink();

        Assert.IsFalse(new OfflineOptions { Sink = sink, Frames = 0L }.RunsUntilIdle, "zero is a stated length");
        Assert.IsFalse(new OfflineOptions { Sink = sink, Duration = 0d }.RunsUntilIdle);
        Assert.IsTrue(new OfflineOptions { Sink = sink }.RunsUntilIdle);
        Assert.AreEqual(0L, new OfflineOptions { Sink = sink, Frames = 0L }.ResolveFrameCount());
    }

    [TestMethod]
    public void HasScheduledWorkAnswersForTheSceneItself()
    {
        var scene = new OverrunScene();
        scene.Load(TestEnvironment.Stub());

        Assert.IsFalse(scene.HasScheduledWork, "nothing is scheduled until OnStart runs, inside the first tick");

        scene.Tick(new TickContext(FixedStepTiming.Tick(0L, 60d)));

        Assert.IsTrue(scene.HasScheduledWork);

        for (var frame = 1L; frame <= 31L; frame++)
        {
            scene.Tick(new TickContext(FixedStepTiming.Tick(frame, 60d)));
        }

        Assert.IsFalse(scene.HasScheduledWork, "and it is false in the tick the last routine completed in");
        Assert.AreEqual(31L, scene.FinishedAt);
    }

    /// <summary>
    /// A scene whose beats are constants and whose real length is longer than their sum.
    /// </summary>
    /// <remarks>
    /// The excess is one frame, and it comes from the rejoin: <c>Wait.For</c> over a helper routine
    /// releases in the pass after the child's last one. A scene doing this three or four times, as a
    /// real slide does, drifts by three or four frames and has no way to notice.
    /// </remarks>
    private sealed class OverrunScene : Scene
    {
        internal const double FirstBeatSeconds = 0.25d;
        internal const double SecondBeatSeconds = 0.25d;

        /// <summary>The length the scene can compute about itself, and it is short.</summary>
        internal const double HandSummedSeconds = FirstBeatSeconds + SecondBeatSeconds;

        internal long FinishedAt { get; private set; } = -1L;

        internal long CurrentFrame { get; private set; } = -1L;

        protected override void OnStart() => Start(Talk());

        protected override void Update(in TickContext tick) => CurrentFrame = tick.Time.Frame;

        private IEnumerator<Wait> Talk()
        {
            yield return Wait.Seconds(FirstBeatSeconds);
            yield return Wait.For(SecondBeat());
            FinishedAt = CurrentFrame;
        }

        private IEnumerator<Wait> SecondBeat()
        {
            yield return Wait.Seconds(SecondBeatSeconds);
        }
    }

    /// <summary>A scene whose only scheduled work is a tween.</summary>
    private sealed class TweenOnlyScene : Scene
    {
        internal double Value { get; private set; } = double.NaN;

        protected override void OnStart() => Animate(v => Value = v, from: 0d, to: 1d, duration: 0.25d);
    }

    /// <summary>A scene that schedules nothing at all.</summary>
    private sealed class IdleScene : Scene
    {
        internal int StartCount { get; private set; }

        protected override void OnStart() => StartCount++;
    }

    /// <summary>A scene holding a routine that never terminates.</summary>
    private sealed class NeverEndingScene : Scene
    {
        protected override void OnStart() => Start(Forever());

        private static IEnumerator<Wait> Forever()
        {
            while (true)
            {
                yield return Wait.NextFrame;
            }
        }
    }

    /// <summary>A scene whose one routine is paused immediately and never resumed.</summary>
    private sealed class PausedScene : Scene
    {
        protected override void OnStart() => Start(Parked()).Pause();

        private static IEnumerator<Wait> Parked()
        {
            yield return Wait.NextFrame;
        }
    }
}
