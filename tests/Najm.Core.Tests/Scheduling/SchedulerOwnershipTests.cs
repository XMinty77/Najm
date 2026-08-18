namespace Najm.Core.Tests.Scheduling;

/// <summary>Covers who owns scheduled work, when it is suspended, and when it is cancelled.</summary>
[TestClass]
public sealed class SchedulerOwnershipTests
{
    [TestMethod]
    public void DisablingTheOwnerSuspendsItsRoutineAndTweenAndReenablingResumesInPlace()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var releasedAt = -1L;
        var value = float.NaN;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Seconds(0.5d);
            releasedAt = harness.Frame;
        }

        node.Start(Routine());
        node.Animate(v => value = v, from: 0f, to: 60f, duration: 1d);

        // Frames 0-10. The tween was created before frame 0, so all eleven of these passes advance
        // it. The routine's wait does not exist until frame 0's coroutine pass creates it, so only
        // the ten passes after that accumulate. The one-pass difference is not a fencepost bug: it
        // is the "first Dt at the next tween pass" rule meeting the "first resume creates the wait"
        // rule, and disabling the owner must not disturb either count.
        harness.Tick(11);
        var frozen = value;
        Assert.AreEqual(11f, frozen, 1e-4f);

        node.Enabled = false;
        harness.Tick(30);

        Assert.AreEqual(-1L, releasedAt, "the wait is not evaluated while the owner is disabled");
        Assert.AreEqual(frozen, value, "and tween time freezes with it");

        node.Enabled = true;
        harness.Tick(20);

        Assert.AreEqual(
            60L,
            releasedAt,
            "ten accumulating passes before the disable plus twenty after it are the thirty 0.5 s needs");
        Assert.AreEqual(
            31f,
            value,
            1e-4f,
            "eleven advancing passes before the disable plus twenty after it, on a sixty-tick ramp");
    }

    [TestMethod]
    public void DisablingAnAncestorSuspendsWorkOwnedByItsDescendants()
    {
        var harness = new SchedulerHarness().Load();
        var middle = harness.Root.Add(new Node2D());
        var leaf = middle.Add(new Node2D());
        var resumes = 0;
        var value = float.NaN;

        IEnumerator<Wait> Routine()
        {
            while (true)
            {
                resumes++;
                yield return Wait.NextFrame;
            }
        }

        leaf.Start(Routine());
        leaf.Animate(v => value = v, from: 0f, to: 60f, duration: 1d);

        harness.Tick(10);
        var resumesBefore = resumes;
        var frozen = value;

        middle.Enabled = false;
        harness.Tick(20);

        Assert.AreEqual(resumesBefore, resumes, "an ancestor's Enabled reaches everything below it");
        Assert.AreEqual(frozen, value);

        middle.Enabled = true;
        harness.Tick(5);

        Assert.AreEqual(resumesBefore + 5, resumes);
        Assert.IsGreaterThan(frozen, value, "and the tween picks up where it left off");
    }

    [TestMethod]
    public void ASceneOwnedRoutineIsUnaffectedByADisabledNode()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            while (true)
            {
                resumes++;
                yield return Wait.NextFrame;
            }
        }

        harness.Scene.Start(Routine());
        node.Enabled = false;
        harness.Root.Enabled = false;
        harness.Tick(5);

        Assert.AreEqual(5, resumes, "scene lifetime has no owner to be disabled");
    }

    [TestMethod]
    public void SceneOwnedRoutinesAreCancelledAtStop()
    {
        var harness = new SchedulerHarness().Load();
        var log = new List<string>();

        IEnumerator<Wait> Routine()
        {
            try
            {
                while (true)
                {
                    yield return Wait.NextFrame;
                }
            }
            finally
            {
                log.Add("finally");
            }
        }

        var handle = harness.Scene.Start(Routine());
        var value = float.NaN;
        var tween = harness.Scene.Animate(v => value = v, from: 0f, to: 60f, duration: 10d);
        harness.Tick(5);
        var atStop = value;

        harness.Scene.StopAction = _ => log.Add("stop-hook");
        harness.Scene.Stop();

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.AreEqual(RoutineStatus.Cancelled, tween.Status);
        Assert.AreEqual(atStop, value, "a cancelled tween stops at its current value");
        CollectionAssert.AreEqual(
            new[] { "stop-hook", "finally" },
            log,
            "the author's stop hook runs first, then the scene's scheduled work is torn down");
    }

    [TestMethod]
    public void SceneOwnedRoutinesAreCancelledEvenWhenTheStopHookThrows()
    {
        var harness = new SchedulerHarness().Load();
        var cleanupRan = false;

        IEnumerator<Wait> Routine()
        {
            try
            {
                while (true)
                {
                    yield return Wait.NextFrame;
                }
            }
            finally
            {
                cleanupRan = true;
            }
        }

        var handle = harness.Scene.Start(Routine());
        harness.Tick();

        harness.Scene.StopAction = _ => throw new InvalidOperationException("stop hook");
        Assert.ThrowsExactly<InvalidOperationException>(harness.Scene.Stop);

        Assert.IsTrue(cleanupRan, "an enumerator that is never disposed is a finally that never runs");
        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
    }

    [TestMethod]
    public void ARoutineStartedAtLoadIsCancelledAtUnloadEvenIfTheSceneNeverStarted()
    {
        var scene = new SchedulerScene();
        var cleanupRan = false;

        IEnumerator<Wait> Routine()
        {
            try
            {
                yield return Wait.NextFrame;
            }
            finally
            {
                cleanupRan = true;
            }
        }

        var handle = default(CoroutineHandle);
        var layer = scene.Layers.Add(new LoadStartingLayer(s => handle = s.Start(Routine())));
        scene.Load(TestEnvironment.Stub());

        Assert.IsTrue(layer.Attached);
        Assert.AreEqual(RoutineStatus.Running, handle.Status);

        // No tick, so Stop is never reached through the started path; unload has to clean up on
        // its own or the enumerator is simply dropped.
        scene.Unload();

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.IsFalse(cleanupRan, "a routine whose body never began has no finally to run");
    }

    [TestMethod]
    public void NodeOwnedRoutinesAndTweensAreCancelledAtDetach()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var cleanupRan = false;
        var value = float.NaN;

        IEnumerator<Wait> Routine()
        {
            try
            {
                while (true)
                {
                    yield return Wait.NextFrame;
                }
            }
            finally
            {
                cleanupRan = true;
            }
        }

        var handle = node.Start(Routine());
        var tween = node.Animate(v => value = v, from: 0f, to: 60f, duration: 10d);
        harness.Tick(5);
        var atDetach = value;

        Assert.IsTrue(harness.Root.Remove(node));

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.IsTrue(cleanupRan, "detach disposes the enumerator, so the author's finally runs");
        Assert.AreEqual(RoutineStatus.Cancelled, tween.Status);
        Assert.AreEqual(atDetach, value, "a detached tween stops at its current value");

        harness.Tick(5);

        Assert.AreEqual(atDetach, value);
    }

    [TestMethod]
    public void DetachingAnAncestorCancelsWorkOwnedByItsDescendants()
    {
        var harness = new SchedulerHarness().Load();
        var middle = harness.Root.Add(new Node2D());
        var leaf = middle.Add(new Node2D());

        var handle = leaf.Start(Forever());
        var tween = leaf.Animate(_ => { }, from: 0f, to: 1f, duration: 10d);
        harness.Tick();

        Assert.IsTrue(harness.Root.Remove(middle));

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.AreEqual(RoutineStatus.Cancelled, tween.Status);
    }

    [TestMethod]
    public void DetachingASiblingLeavesOtherOwnersAlone()
    {
        var harness = new SchedulerHarness().Load();
        var first = harness.Root.Add(new Node2D());
        var second = harness.Root.Add(new Node2D());

        var kept = second.Start(Forever());
        var dropped = first.Start(Forever());
        harness.Tick();

        Assert.IsTrue(harness.Root.Remove(first));

        Assert.AreEqual(RoutineStatus.Cancelled, dropped.Status);
        Assert.AreEqual(RoutineStatus.Running, kept.Status);
    }

    [TestMethod]
    public void WaitForARoutineStartsTheChildWithTheParentsOwner()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var childCleanupRan = false;

        IEnumerator<Wait> Child()
        {
            try
            {
                while (true)
                {
                    yield return Wait.NextFrame;
                }
            }
            finally
            {
                childCleanupRan = true;
            }
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(Child());
        }

        node.Start(Parent());
        harness.Tick();

        Assert.IsTrue(harness.Root.Remove(node));

        Assert.IsTrue(childCleanupRan, "the child inherited the parent's node lifetime, so detach took it too");
    }

    [TestMethod]
    public void ARoutineMayStructurallyMutateAndTheEditLandsAtTheEndOfUpdate()
    {
        var harness = new SchedulerHarness().Load();
        var added = new Node2D();
        var childCountDuringPass = -1;

        IEnumerator<Wait> Routine()
        {
            harness.Root.Add(added);
            childCountDuringPass = harness.Root.Children.Count;
            yield break;
        }

        harness.Scene.Start(Routine());
        harness.Tick();

        Assert.AreEqual(0, childCountDuringPass, "the edit is deferred like any other made inside Update");
        Assert.AreEqual(1, harness.Root.Children.Count, "and lands in the end-of-update flush");
        Assert.AreSame(harness.Layer, added.Layer);
    }

    private static IEnumerator<Wait> Forever()
    {
        while (true)
        {
            yield return Wait.NextFrame;
        }
    }

    private sealed class LoadStartingLayer : ScreenLayer
    {
        private readonly Action<Scene> onAttach;

        internal LoadStartingLayer(Action<Scene> onAttach) => this.onAttach = onAttach;

        internal bool Attached { get; private set; }

        protected override void OnAttach(Scene scene)
        {
            Attached = true;
            onAttach(scene);
        }
    }
}
