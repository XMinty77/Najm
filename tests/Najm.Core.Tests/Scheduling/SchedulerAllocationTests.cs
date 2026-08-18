using Najm.Core.Tests.Runtime;

namespace Najm.Core.Tests.Scheduling;

/// <summary>Pins the steady-state allocation budget of the two scheduler passes.</summary>
/// <remarks>
/// Starting a routine or a tween allocates once, by construction — an iterator state machine and a
/// record to hold it. What must not allocate is the frame path: polling live routines, resuming
/// them, advancing live tweens, and compacting the queues. Every wait is a struct, both queues are
/// index-walked lists, and the compaction is in place, so a warm tick over a populated scheduler
/// should read exactly zero.
/// </remarks>
[TestClass]
public sealed class SchedulerAllocationTests
{
    [TestMethod]
    public void AWarmTickWithLiveRoutinesAndTweensAllocatesNoManagedBytes()
    {
        // Far longer than the measured run consumes, so nothing reaches a terminal status inside the
        // window and the queues stay populated throughout.
        const double NeverEnds = 1e9d;

        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var node = layer.Root.Add(new Node2D());
        scene.Load(TestEnvironment.Stub());

        var sceneRoutineResumes = 0;
        var nodeRoutineResumes = 0;
        var sceneTweenWrites = 0;
        var nodeTweenWrites = 0;
        var joinPolls = 0;

        IEnumerator<Wait> EveryFrame(Action onResume)
        {
            while (true)
            {
                onResume();
                yield return Wait.NextFrame;
            }
        }

        IEnumerator<Wait> Accumulating()
        {
            yield return Wait.Seconds(NeverEnds);
        }

        // Scene-owned and node-owned alike, so the pass walks both the no-owner path and the
        // ancestor-chain eligibility path on every tick.
        var sceneRoutine = scene.Start(EveryFrame(() => sceneRoutineResumes++));
        var secondsRoutine = scene.Start(Accumulating());
        var nodeRoutine = node.Start(EveryFrame(() => nodeRoutineResumes++));

        var sceneTween = scene.Animate(_ => sceneTweenWrites++, from: 0f, to: 1f, duration: NeverEnds);
        var nodeTween = node.Animate(_ => nodeTweenWrites++, from: 0f, to: 1f, duration: NeverEnds);

        IEnumerator<Wait> Joined()
        {
            joinPolls++;
            yield return Wait.For(sceneTween);
        }

        var joiner = scene.Start(Joined());

        const int WarmTicks = 64;
        for (var frame = 0; frame < WarmTicks; frame++)
        {
            scene.Tick(RuntimeTicks.At(frame));
        }

        // The probe runs the body extra times — warm, settle, and once more per retried window — so
        // the tick count the counters must match is the probe's own total, not a constant.
        var ticked = WarmTicks;
        var reading = AllocationProbe.AssertNoneAllocated(
            100_000,
            () =>
            {
                scene.Tick(RuntimeTicks.At(ticked));
                ticked++;
            },
            "A warm tick over live routines and tweens");

        Assert.AreEqual(WarmTicks + reading.Invocations, ticked);
        Assert.AreEqual(ticked, sceneRoutineResumes, "the scene-owned routine resumed on every tick");
        Assert.AreEqual(ticked, nodeRoutineResumes, "and so did the node-owned one");
        Assert.AreEqual(
            ticked + 1,
            sceneTweenWrites,
            "one write per tick, plus the from-value applied at the Animate call site");
        Assert.AreEqual(ticked + 1, nodeTweenWrites);
        Assert.AreEqual(1, joinPolls, "the joiner is still suspended on its animation, polled every pass");

        Assert.AreEqual(RoutineStatus.Running, sceneRoutine.Status);
        Assert.AreEqual(RoutineStatus.Running, secondsRoutine.Status);
        Assert.AreEqual(RoutineStatus.Running, nodeRoutine.Status);
        Assert.AreEqual(RoutineStatus.Running, joiner.Status);
        Assert.AreEqual(RoutineStatus.Running, sceneTween.Status);
        Assert.AreEqual(RoutineStatus.Running, nodeTween.Status);
    }

    [TestMethod]
    public void AWarmTickWithASuspendedSubtreeAllocatesNoManagedBytes()
    {
        // The ineligible path is walked every frame too — a disabled subtree still has its owners'
        // ancestor chains tested — so it carries the same budget as the eligible one.
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var parent = layer.Root.Add(new Node2D());
        var child = parent.Add(new Node2D());
        scene.Load(TestEnvironment.Stub());

        var resumes = 0;

        IEnumerator<Wait> EveryFrame()
        {
            while (true)
            {
                resumes++;
                yield return Wait.NextFrame;
            }
        }

        child.Start(EveryFrame());
        child.Animate(_ => { }, from: 0f, to: 1f, duration: 1e9d);
        parent.Enabled = false;

        for (var frame = 0; frame < 64; frame++)
        {
            scene.Tick(RuntimeTicks.At(frame));
        }

        var ticked = 64;
        AllocationProbe.AssertNoneAllocated(
            50_000,
            () =>
            {
                scene.Tick(RuntimeTicks.At(ticked));
                ticked++;
            },
            "A warm tick over a suspended subtree");

        Assert.AreEqual(0, resumes, "the whole point: an ancestor is disabled, so nothing resumed");
    }
}
