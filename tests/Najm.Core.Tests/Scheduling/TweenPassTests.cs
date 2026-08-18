using Najm.Utils;

namespace Najm.Core.Tests.Scheduling;

/// <summary>Covers the tween pass, its placement before the coroutine pass, and handle semantics.</summary>
[TestClass]
public sealed class TweenPassTests
{
    [TestMethod]
    public void ChainedTweensJoinedByWaitForOccupyExactlySixtyTicks()
    {
        var harness = new SchedulerHarness();
        var value = float.NaN;
        var firstTweenCreatedAt = -1L;
        var firstTweenEndedAt = -1L;
        var firstJoinReleasedAt = -1L;
        var chainCompletedAt = -1L;

        IEnumerator<Wait> Chain()
        {
            firstTweenCreatedAt = harness.Frame;

            // A linear ramp writes its exact to-value only on the pass that completes it, so this
            // is a clean detector for the frame the tween ended on.
            var first = harness.Scene.Animate(
                v =>
                {
                    value = v;
                    if (v == 1f && firstTweenEndedAt < 0)
                    {
                        firstTweenEndedAt = harness.Frame;
                    }
                },
                from: 0f,
                to: 1f,
                duration: 0.5d);
            yield return Wait.For(first);

            firstJoinReleasedAt = harness.Frame;
            yield return Wait.For(harness.Scene.Animate(v => value = v, from: 1f, to: 2f, duration: 0.5d));
            chainCompletedAt = harness.Frame;
        }

        harness.Scene.StartAction = scene => scene.Start(Chain());
        harness.Load();

        harness.TickUntil(() => chainCompletedAt >= 0);

        // Both tweens are created inside a coroutine pass, whose frame's tween pass has already
        // run, so each consumes its thirty ticks starting the frame after: 1-30, then 31-60.
        Assert.AreEqual(0L, firstTweenCreatedAt);
        Assert.AreEqual(30L, firstTweenEndedAt, "0.5 s at 60 fps is exactly 30 consumed ticks");
        Assert.AreEqual(
            firstTweenEndedAt,
            firstJoinReleasedAt,
            "the join must release in the frame the tween ended, not the one after. This is what the "
            + "tween pass running before the coroutine pass buys, and the only thing that makes the "
            + "second tween start without a blank frame between the two.");
        Assert.AreEqual(
            60L,
            chainCompletedAt - firstTweenCreatedAt,
            "0.5 s + 0.5 s at 60 fps is exactly 60 ticks, not 61");
        Assert.AreEqual(2f, value, "a tween always lands exactly on its to-value");
    }

    [TestMethod]
    public void ATweenThatEndsThisFrameReleasesItsWaiterInTheSameFramesPass()
    {
        var harness = new SchedulerHarness().Load();
        var tweenEndedAt = -1L;
        var waiterResumedAt = -1L;
        var handle = harness.Scene.Animate(
            v =>
            {
                if (v >= 1f)
                {
                    tweenEndedAt = harness.Frame;
                }
            },
            from: 0f,
            to: 1f,
            duration: 0.5d);

        IEnumerator<Wait> Waiter()
        {
            yield return Wait.For(handle);
            waiterResumedAt = harness.Frame;
        }

        harness.Scene.Start(Waiter());
        harness.TickUntil(() => waiterResumedAt >= 0);

        // The tween was created before frame 0, so frame 0's pass consumes its first Dt and frame
        // k's pass leaves it at (k+1)/60 s. It reaches 0.5 s when k = 29 — thirty consumed ticks,
        // frames 0 through 29.
        Assert.AreEqual(29L, tweenEndedAt, "thirty consumed ticks, the first of them frame 0's");
        Assert.AreEqual(tweenEndedAt, waiterResumedAt, "no blank frame between the tween ending and the join");
    }

    [TestMethod]
    public void WhereATweenEndsDependsOnWhichPassConsumedItsFirstDelta()
    {
        // Duration fixes the number of consumed ticks, never the frame index it lands on. Both
        // tweens below run 0.5 s and therefore consume exactly 30 ticks; they finish a frame apart
        // only because one of them was created a pass later than the other.
        var harness = new SchedulerHarness();
        var startedBeforeFrameZero = -1L;
        var startedInsideFrameZero = -1L;

        var early = default(AnimationHandle);
        var late = default(AnimationHandle);

        IEnumerator<Wait> StartLate()
        {
            late = harness.Scene.Animate(_ => { }, from: 0f, to: 1f, duration: 0.5d);
            yield break;
        }

        harness.Scene.StartAction = scene => scene.Start(StartLate());
        harness.Load();
        early = harness.Scene.Animate(_ => { }, from: 0f, to: 1f, duration: 0.5d);

        harness.TickUntil(() =>
        {
            if (startedBeforeFrameZero < 0 && early.Status == RoutineStatus.Completed)
            {
                startedBeforeFrameZero = harness.Frame;
            }
            if (startedInsideFrameZero < 0 && late.IsValid && late.Status == RoutineStatus.Completed)
            {
                startedInsideFrameZero = harness.Frame;
            }

            return startedBeforeFrameZero >= 0 && startedInsideFrameZero >= 0;
        });

        Assert.AreEqual(
            29L,
            startedBeforeFrameZero,
            "created before frame 0, so frame 0's tween pass is its first: it consumes frames 0-29");
        Assert.AreEqual(
            30L,
            startedInsideFrameZero,
            "created in frame 0's coroutine pass, which the tween pass already ran: frames 1-30");
    }

    [TestMethod]
    public void AnimateAppliesTheFromValueAtTheCallSite()
    {
        var harness = new SchedulerHarness().Load();
        var writes = new List<float>();

        var handle = harness.Scene.Animate(writes.Add, from: 7f, to: 9f, duration: 1d);

        CollectionAssert.AreEqual(
            new[] { 7f },
            writes,
            "the from-value is written synchronously so the property never shows a stale frame");
        Assert.AreEqual(RoutineStatus.Running, handle.Status);

        harness.Tick();

        Assert.HasCount(2, writes, "the first delta is consumed at the next tween pass");
    }

    [TestMethod]
    public void ATweenAdvancesOnSimulationTimeAndLandsExactlyOnItsToValue()
    {
        var harness = new SchedulerHarness().Load();
        var value = float.NaN;
        var handle = harness.Scene.Animate(v => value = v, from: 0f, to: 60f, duration: 1d);

        harness.Tick(30);

        Assert.AreEqual(30f, value, 1e-4f, "linear halfway at half the duration");
        Assert.AreEqual(RoutineStatus.Running, handle.Status);

        harness.Tick(30);

        Assert.AreEqual(60f, value);
        Assert.AreEqual(RoutineStatus.Completed, handle.Status, "1 s at 60 fps is exactly 60 ticks");
    }

    [TestMethod]
    public void CompleteInvokesTheSetterOnceWithTheFinalValue()
    {
        var harness = new SchedulerHarness().Load();
        var writes = new List<float>();
        var handle = harness.Scene.Animate(writes.Add, from: 0f, to: 5f, duration: 10d);

        harness.Tick(3);
        var beforeComplete = writes.Count;

        handle.Complete();

        Assert.HasCount(beforeComplete + 1, writes);
        Assert.AreEqual(5f, writes[^1]);
        Assert.AreEqual(RoutineStatus.Completed, handle.Status);

        handle.Complete();
        harness.Tick(3);

        Assert.HasCount(beforeComplete + 1, writes, "a completed tween is never written again");
    }

    [TestMethod]
    public void CancelLeavesTheTweenAtItsCurrentValue()
    {
        var harness = new SchedulerHarness().Load();
        var value = float.NaN;
        var handle = harness.Scene.Animate(v => value = v, from: 0f, to: 60f, duration: 1d);

        harness.Tick(15);
        var atCancel = value;
        handle.Cancel();

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.AreEqual(atCancel, value, "cancelling writes nothing at all");

        harness.Tick(30);

        Assert.AreEqual(atCancel, value, "and the tween never advances again");
    }

    [TestMethod]
    public void PauseFreezesTweenTimeAndResumeContinuesInPlace()
    {
        var harness = new SchedulerHarness().Load();
        var value = float.NaN;
        var handle = harness.Scene.Animate(v => value = v, from: 0f, to: 60f, duration: 1d);

        harness.Tick(15);
        var atPause = value;
        handle.Pause();
        harness.Tick(30);

        Assert.AreEqual(atPause, value);

        handle.Resume();
        harness.Tick(15);

        Assert.AreEqual(30f, value, 1e-4f, "thirty eligible ticks of a sixty-tick ramp");
    }

    [TestMethod]
    public void AZeroDurationTweenCompletesOnItsFirstPass()
    {
        var harness = new SchedulerHarness().Load();
        var writes = new List<float>();
        var handle = harness.Scene.Animate(writes.Add, from: 2f, to: 3f, duration: 0d);

        harness.Tick();

        Assert.AreEqual(RoutineStatus.Completed, handle.Status);
        CollectionAssert.AreEqual(new[] { 2f, 3f }, writes);
    }

    [TestMethod]
    public void ATweenUsesTheEasingCurveItWasGiven()
    {
        var harness = new SchedulerHarness().Load();
        var eased = float.NaN;
        var linear = float.NaN;

        harness.Scene.Animate(v => eased = v, from: 0f, to: 1f, duration: 1d, ease: Ease.InQuad);
        harness.Scene.Animate(v => linear = v, from: 0f, to: 1f, duration: 1d);
        harness.Tick(30);

        Assert.AreEqual(0.5f, linear, 1e-4f);
        Assert.AreEqual(0.25f, eased, 1e-3f, "InQuad at t = 0.5 is 0.25");
    }

    [TestMethod]
    public void ATweenAcceptsACustomTimingFunction()
    {
        var harness = new SchedulerHarness().Load();
        var value = float.NaN;

        harness.Scene.Animate(v => value = v, 0f, 1f, 1d, new ConstantHalf());
        harness.Tick(30);

        Assert.AreEqual(0.5f, value);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => harness.Scene.Animate(_ => { }, 0f, 1f, 1d, (ITimingFunction)null!));
    }

    [TestMethod]
    public void AnimateValidatesItsArguments()
    {
        var harness = new SchedulerHarness().Load();

        Assert.ThrowsExactly<ArgumentNullException>(() => harness.Scene.Animate(null!, 0f, 1f, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => harness.Scene.Animate(_ => { }, float.NaN, 1f, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => harness.Scene.Animate(_ => { }, 0f, float.PositiveInfinity, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => harness.Scene.Animate(_ => { }, 0f, 1f, -1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => harness.Scene.Animate(_ => { }, 0f, 1f, double.NaN));
    }

    [TestMethod]
    public void StepCompletesAJoinedAnimation()
    {
        var harness = new SchedulerHarness().Load();
        var value = float.NaN;
        var released = false;
        var tween = default(AnimationHandle);

        IEnumerator<Wait> Routine()
        {
            tween = harness.Scene.Animate(v => value = v, from: 0f, to: 4f, duration: 10d);
            yield return Wait.For(tween);
            released = true;
        }

        var handle = harness.Scene.Start(Routine());
        harness.Tick(3);

        handle.Step();

        Assert.IsTrue(released);
        Assert.AreEqual(4f, value, "the joined animation jumped to its end");
        Assert.AreEqual(RoutineStatus.Completed, tween.Status);
    }

    [TestMethod]
    public void ATweenStartedDuringTheTweenPassTakesItsFirstDeltaNextFrame()
    {
        var harness = new SchedulerHarness().Load();
        var outerWrites = 0;
        var secondWrites = 0;

        harness.Scene.Animate(
            _ =>
            {
                outerWrites++;
                if (outerWrites == 2)
                {
                    // The second write is the one the tween pass makes, so this start happens
                    // inside the pass rather than at a call site outside it.
                    harness.Scene.Animate(_ => secondWrites++, from: 0f, to: 1f, duration: 10d);
                }
            },
            from: 0f,
            to: 1f,
            duration: 10d);

        harness.Tick();

        Assert.AreEqual(2, outerWrites);
        Assert.AreEqual(1, secondWrites, "only the synchronous from-value write; the pass had already been sized");

        harness.Tick();

        Assert.AreEqual(2, secondWrites);
    }

    [TestMethod]
    public void TheDefaultHandleRefersToNoAnimation()
    {
        var handle = default(AnimationHandle);

        Assert.IsFalse(handle.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.Status);
        Assert.ThrowsExactly<InvalidOperationException>(handle.Complete);
        Assert.ThrowsExactly<ArgumentException>(() => Wait.For(handle));
    }

    private sealed class ConstantHalf : ITimingFunction
    {
        public float Evaluate(float progress) => 0.5f;
    }
}
