namespace Najm.Core.Tests.Scheduling;

/// <summary>Covers the drain order, the wait vocabulary, and the exactness rules of §10.2–§10.3.</summary>
[TestClass]
public sealed class CoroutinePassTests
{
    [TestMethod]
    public void ThePassDrainsInEnqueueOrderAndResumesChildrenStartedDuringItInTheSamePass()
    {
        var harness = new SchedulerHarness();
        var log = new List<string>();

        IEnumerator<Wait> Leaf(string name)
        {
            log.Add(name);
            yield break;
        }

        IEnumerator<Wait> Spawner()
        {
            log.Add("a");

            // Started explicitly during the pass: appended behind the routines already queued.
            harness.Scene.Start(Leaf("d"));

            // Started implicitly by the wait itself, and therefore behind "d".
            yield return Wait.For(Leaf("e"));
            log.Add("a-resumed");
        }

        harness.Scene.StartAction = scene =>
        {
            scene.Start(Spawner());
            scene.Start(Leaf("b"));
            scene.Start(Leaf("c"));
        };
        harness.Load();

        harness.Tick();
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c", "d", "e" },
            log,
            "the pass must drain to empty in enqueue order, children included");

        harness.Tick();
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e", "a-resumed" }, log);
    }

    [TestMethod]
    public void ARoutineStartedBeforeThePassTakesItsFirstResumeInThatSameFrame()
    {
        var harness = new SchedulerHarness().Load();
        var resumedAt = -1L;

        IEnumerator<Wait> Routine()
        {
            resumedAt = harness.Frame;
            yield break;
        }

        harness.Scene.Start(Routine());
        harness.Tick();

        Assert.AreEqual(0L, resumedAt, "a routine queued before the pass resumes in the first frame's pass");
    }

    [TestMethod]
    public void NextFrameResumesInEveryFollowingPass()
    {
        var harness = new SchedulerHarness().Load();
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
        harness.Tick(5);

        Assert.AreEqual(5, resumes);
    }

    [TestMethod]
    public void SecondsIsExactlyThirtyTicksAtSixtyFramesPerSecond()
    {
        var harness = new SchedulerHarness().Load();
        var releasedAt = -1L;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Seconds(0.5d);
            releasedAt = harness.Frame;
        }

        harness.Scene.Start(Routine());

        // Frame 0 is the routine's first resume, where the wait is created; the accumulation runs
        // over the frames after it.
        harness.Tick();
        harness.Tick(29);
        Assert.AreEqual(-1L, releasedAt, "twenty-nine accumulated ticks are half a frame short of 0.5 s");

        harness.Tick();
        Assert.AreEqual(30L, releasedAt, "0.5 s at 60 fps is exactly 30 ticks");
    }

    [TestMethod]
    public void ChainedSecondsWaitsCarryNoFractionalRemainder()
    {
        var harness = new SchedulerHarness().Load();
        var released = new List<long>();

        IEnumerator<Wait> Routine()
        {
            for (var index = 0; index < 3; index++)
            {
                yield return Wait.Seconds(0.5d);
                released.Add(harness.Frame);
            }
        }

        harness.Scene.Start(Routine());
        harness.Tick(91);

        CollectionAssert.AreEqual(
            new[] { 30L, 60L, 90L },
            released,
            "each wait quantizes to the tick grid on its own; no remainder carries into the next");
    }

    [TestMethod]
    public void AZeroSecondsWaitReleasesAtTheNextPass()
    {
        var harness = new SchedulerHarness().Load();
        var releasedAt = -1L;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Seconds(0d);
            releasedAt = harness.Frame;
        }

        harness.Scene.Start(Routine());
        harness.Tick(2);

        Assert.AreEqual(1L, releasedAt);
    }

    [TestMethod]
    public void PauseFreezesSecondsAccumulationAndResumeContinuesInPlace()
    {
        var harness = new SchedulerHarness().Load();
        var releasedAt = -1L;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Seconds(0.5d);
            releasedAt = harness.Frame;
        }

        var handle = harness.Scene.Start(Routine());

        harness.Tick();
        harness.Tick(10);
        handle.Pause();
        harness.Tick(30);
        Assert.AreEqual(-1L, releasedAt, "a paused routine's wait is not evaluated at all");

        handle.Resume();
        harness.Tick(20);

        Assert.AreEqual(
            60L,
            releasedAt,
            "ten eligible ticks before the pause plus twenty after it are the thirty the wait needs");
    }

    [TestMethod]
    public void WaitForReleasesOnACompletedChildAndTheParentReadsItsStatus()
    {
        var harness = new SchedulerHarness().Load();
        var observed = (RoutineStatus?)null;
        var child = default(CoroutineHandle);

        IEnumerator<Wait> Child()
        {
            yield return Wait.NextFrame;
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(child);
            observed = child.Status;
        }

        child = harness.Scene.Start(Child());
        harness.Scene.Start(Parent());

        harness.Tick(3);

        Assert.AreEqual(RoutineStatus.Completed, observed);
    }

    [TestMethod]
    public void WaitForReleasesOnACancelledChildWithoutKillingTheParent()
    {
        var harness = new SchedulerHarness().Load();
        var observed = (RoutineStatus?)null;
        var child = default(CoroutineHandle);

        IEnumerator<Wait> Child()
        {
            while (true)
            {
                yield return Wait.NextFrame;
            }
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(child);
            observed = child.Status;
        }

        child = harness.Scene.Start(Child());
        var parent = harness.Scene.Start(Parent());

        harness.Tick();
        child.Cancel();
        harness.Tick();

        Assert.AreEqual(RoutineStatus.Cancelled, observed, "the parent resumes and can branch on the child's fate");
        Assert.AreEqual(RoutineStatus.Completed, parent.Status);
    }

    [TestMethod]
    public void WaitForReleasesOnAFaultedChildWithoutKillingTheParent()
    {
        var harness = new SchedulerHarness().Load();
        var observed = (RoutineStatus?)null;
        var child = default(CoroutineHandle);

        IEnumerator<Wait> Child()
        {
            yield return Wait.NextFrame;
            throw new InvalidOperationException("child fault");
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(child);
            observed = child.Status;
        }

        child = harness.Scene.Start(Child());
        var parent = harness.Scene.Start(Parent());
        harness.Tick();

        // Faulting the child through Step rather than through a pass keeps the throw off the
        // driver's tick, which the scene would otherwise treat as its own fault and refuse to
        // continue from. What is under test here is the release, not the propagation.
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => child.Step());
        Assert.AreEqual("child fault", failure.Message);
        Assert.AreEqual(RoutineStatus.Faulted, child.Status);

        harness.Tick();

        Assert.AreEqual(RoutineStatus.Faulted, observed);
        Assert.AreEqual(RoutineStatus.Completed, parent.Status);
    }

    [TestMethod]
    public void CancelDisposesTheEnumeratorAtTheCallSite()
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

        // The first resume is what puts the iterator inside its try block; a routine that never
        // resumed has no cleanup to run.
        harness.Tick();

        log.Add("before-cancel");
        handle.Cancel();
        log.Add("after-cancel");

        CollectionAssert.AreEqual(
            new[] { "before-cancel", "finally", "after-cancel" },
            log,
            "Dispose runs at the Cancel call site, not at some later collection");
        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
    }

    [TestMethod]
    public void ACancelledRoutineNeverResumesAgain()
    {
        var harness = new SchedulerHarness().Load();
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            while (true)
            {
                resumes++;
                yield return Wait.NextFrame;
            }
        }

        var handle = harness.Scene.Start(Routine());
        harness.Tick(3);
        handle.Cancel();
        harness.Tick(3);

        Assert.AreEqual(3, resumes);
    }

    [TestMethod]
    public void AFaultMarksTheRoutineFaultedRunsFinallyAndRethrowsToTheDriver()
    {
        var harness = new SchedulerHarness().Load();
        var log = new List<string>();

        IEnumerator<Wait> Routine()
        {
            try
            {
                yield return Wait.NextFrame;
                throw new InvalidOperationException("routine fault");
            }
            finally
            {
                log.Add("finally");
            }
        }

        var handle = harness.Scene.Start(Routine());
        harness.Tick();

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Tick());

        Assert.AreEqual("routine fault", failure.Message, "the driver sees the author's exception, unwrapped");
        Assert.AreEqual(RoutineStatus.Faulted, handle.Status);
        CollectionAssert.AreEqual(new[] { "finally" }, log, "cleanup runs exactly once");
    }

    [TestMethod]
    public void StepFastForwardsTheCurrentWaitAndResumesExactlyOnce()
    {
        var harness = new SchedulerHarness().Load();
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            resumes++;
            yield return Wait.Seconds(10d);
            resumes++;
            yield return Wait.NextFrame;
            resumes++;
        }

        var handle = harness.Scene.Start(Routine());

        Assert.IsTrue(handle.Step(), "a routine that never resumed performs its first resume");
        Assert.AreEqual(1, resumes);

        Assert.IsTrue(handle.Step(), "the ten-second wait is deemed satisfied");
        Assert.AreEqual(2, resumes);

        Assert.IsTrue(handle.Step());
        Assert.AreEqual(3, resumes);
        Assert.AreEqual(RoutineStatus.Completed, handle.Status);

        Assert.IsFalse(handle.Step(), "stepping a terminal routine reports that there was nothing to do");
    }

    [TestMethod]
    public void StepLeavesAPausedRoutinePaused()
    {
        var harness = new SchedulerHarness().Load();
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            while (true)
            {
                resumes++;
                yield return Wait.NextFrame;
            }
        }

        var handle = harness.Scene.Start(Routine());
        handle.Pause();

        Assert.IsTrue(handle.Step());
        Assert.AreEqual(1, resumes);

        harness.Tick(5);

        Assert.AreEqual(1, resumes, "the pass still skips it");
    }

    [TestMethod]
    public void StepReleasesAJoinWithoutForceRunningTheChild()
    {
        var harness = new SchedulerHarness().Load();
        var childResumes = 0;
        var parentReleased = false;
        var child = default(CoroutineHandle);

        IEnumerator<Wait> Child()
        {
            while (true)
            {
                childResumes++;
                yield return Wait.NextFrame;
            }
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(child);
            parentReleased = true;
        }

        child = harness.Scene.Start(Child());
        var parent = harness.Scene.Start(Parent());
        harness.Tick();

        var childResumesBefore = childResumes;
        parent.Step();

        Assert.IsTrue(parentReleased);
        Assert.AreEqual(childResumesBefore, childResumes, "the child is not force-run");
        Assert.AreEqual(RoutineStatus.Running, child.Status, "and continues on its own schedule");
    }

    [TestMethod]
    public void TheDefaultHandleRefersToNoRoutine()
    {
        var handle = default(CoroutineHandle);

        Assert.IsFalse(handle.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.Status);
        Assert.ThrowsExactly<InvalidOperationException>(handle.Cancel);
        Assert.ThrowsExactly<ArgumentException>(() => Wait.For(handle));
    }

    [TestMethod]
    public void WaitsValidateTheirArguments()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Wait.Seconds(-0.001d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Wait.Seconds(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Wait.Seconds(double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentNullException>(() => Wait.For((IEnumerator<Wait>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Wait.Until(null!));
        Assert.AreEqual(Wait.NextFrame, default(Wait), "the default wait is NextFrame, not a broken one");
    }

    [TestMethod]
    public void StartingARoutineRequiresASchedulableScene()
    {
        var scene = new SchedulerScene();

        IEnumerator<Wait> Routine()
        {
            yield break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Start(Routine()));
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Start(null!));

        scene.Load(TestEnvironment.Stub());
        scene.Tick(SchedulerTicks.At(0));
        scene.Stop();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.Start(Routine()));
    }

    [TestMethod]
    public void StartingFromANodeRequiresAnAttachedNode()
    {
        var node = new Node2D();

        IEnumerator<Wait> Routine()
        {
            yield break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => node.Start(Routine()));
    }
}
