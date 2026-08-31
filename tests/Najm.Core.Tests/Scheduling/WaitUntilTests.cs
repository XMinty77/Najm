namespace Najm.Core.Tests.Scheduling;

/// <summary>
/// Pins <see cref="Wait.Until(Func{bool})"/> against the spelling it exists to replace: the frame a
/// routine resumes on must be the same for both, in both directions.
/// </summary>
/// <remarks>
/// Every assertion here is a frame index, not a relative order. A condition test that only asked
/// "did it resume?" would pass under a scheduler that resumed one pass late, which is exactly the
/// defect this member was built for — the one where hoisting a spin into a helper routine retimes
/// it by a frame and nothing at the call site says so.
/// </remarks>
[TestClass]
public sealed class WaitUntilTests
{
    /// <summary>The frame the tests' condition turns true on, chosen to be neither the first nor the last.</summary>
    private const long SettlesAt = 3L;

    [TestMethod]
    public void TheThreeSpellingsOfWaitingForAConditionResumeOnTheFramesTheTableSays()
    {
        var harness = new SchedulerHarness().Load();
        var settled = false;

        // The scene hook runs before the coroutine pass, so the condition is already true when the
        // pass of frame 3 evaluates it.
        harness.Scene.UpdateAction = _ => settled = harness.Frame >= SettlesAt;

        var spinLanded = -1L;
        var nestedLanded = -1L;
        var untilLanded = -1L;

        IEnumerator<Wait> Spin()
        {
            while (!settled)
            {
                yield return Wait.NextFrame;
            }

            spinLanded = harness.Frame;
        }

        IEnumerator<Wait> SpinHelper()
        {
            while (!settled)
            {
                yield return Wait.NextFrame;
            }
        }

        IEnumerator<Wait> Nested()
        {
            yield return Wait.For(SpinHelper());
            nestedLanded = harness.Frame;
        }

        IEnumerator<Wait> Condition()
        {
            yield return Wait.Until(() => settled);
            untilLanded = harness.Frame;
        }

        harness.Scene.Start(Spin());
        harness.Scene.Start(Nested());
        harness.Scene.Start(Condition());
        harness.Tick(6);

        Assert.AreEqual(SettlesAt, spinLanded, "the inline spin resumes in the pass the condition first holds in");
        Assert.AreEqual(
            SettlesAt + 1L,
            nestedLanded,
            "the same spin behind a helper routine rejoins a pass later: the parent is polled ahead "
            + "of the child that completes behind it");
        Assert.AreEqual(SettlesAt, untilLanded, "Until is the spin, not the helper");
    }

    [TestMethod]
    public void APredicateThatAlreadyHoldsCostsNoFrameAtAll()
    {
        var harness = new SchedulerHarness().Load();

        var spinLanded = -1L;
        var nestedLanded = -1L;
        var untilLanded = -1L;

        IEnumerator<Wait> Spin()
        {
            var settled = true;
            while (!settled)
            {
                yield return Wait.NextFrame;
            }

            spinLanded = harness.Frame;
        }

        IEnumerator<Wait> SpinHelper()
        {
            yield break;
        }

        IEnumerator<Wait> Nested()
        {
            yield return Wait.For(SpinHelper());
            nestedLanded = harness.Frame;
        }

        IEnumerator<Wait> Condition()
        {
            yield return Wait.Until(() => true);
            untilLanded = harness.Frame;
        }

        harness.Scene.Start(Spin());
        harness.Scene.Start(Nested());
        harness.Scene.Start(Condition());
        harness.Tick(3);

        Assert.AreEqual(0L, spinLanded, "a spin whose condition already holds never suspends");
        Assert.AreEqual(1L, nestedLanded, "the helper still costs its rejoin frame");
        Assert.AreEqual(
            0L,
            untilLanded,
            "the predicate is evaluated in the pass that yields the wait, so an already-true "
            + "condition costs no frame either — a wait that suspended first would retime every "
            + "spin it replaced");
    }

    [TestMethod]
    public void ThePredicateIsEvaluatedExactlyOncePerEligiblePass()
    {
        var harness = new SchedulerHarness().Load();
        var calls = 0;
        var resumed = false;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Until(() =>
            {
                calls++;
                return false;
            });

            resumed = true;
        }

        harness.Scene.Start(Routine());
        harness.Tick(4);

        Assert.AreEqual(4, calls, "one evaluation in the pass that yielded the wait, then one per pass");
        Assert.IsFalse(resumed, "a predicate that never holds parks the routine; there is no timeout");
    }

    [TestMethod]
    public void PauseStopsThePredicateBeingCalledAndResumeContinuesInPlace()
    {
        var harness = new SchedulerHarness().Load();
        var calls = 0;
        var landed = -1L;
        var ready = false;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Until(() =>
            {
                calls++;
                return ready;
            });

            landed = harness.Frame;
        }

        var handle = harness.Scene.Start(Routine());
        harness.Tick();

        Assert.AreEqual(1, calls);

        handle.Pause();
        ready = true;
        harness.Tick(3);

        Assert.AreEqual(1, calls, "a paused routine's wait is not evaluated at all");
        Assert.AreEqual(-1L, landed);

        handle.Resume();
        harness.Tick();

        Assert.AreEqual(2, calls);
        Assert.AreEqual(4L, landed, "it releases in the first eligible pass after the resume");
    }

    [TestMethod]
    public void ADisabledOwnerStopsThePredicateBeingCalled()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var calls = 0;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Until(() =>
            {
                calls++;
                return false;
            });
        }

        node.Start(Routine());
        harness.Tick();

        Assert.AreEqual(1, calls);

        node.Enabled = false;
        harness.Tick(3);

        Assert.AreEqual(1, calls, "disabling the owner is Pause for everything the subtree owns");

        node.Enabled = true;
        harness.Tick();

        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void StepDeemsAnUntilSatisfiedWithoutCallingThePredicate()
    {
        var harness = new SchedulerHarness().Load();
        var calls = 0;
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            resumes++;
            yield return Wait.Until(() =>
            {
                calls++;
                return false;
            });

            resumes++;
        }

        var handle = harness.Scene.Start(Routine());

        Assert.IsTrue(handle.Step(), "the first step performs the routine's first resume");
        Assert.AreEqual(1, resumes);
        Assert.AreEqual(0, calls, "a step never evaluates a predicate, not even the one it lands on");

        Assert.IsTrue(handle.Step(), "the second step deems the never-true wait satisfied");
        Assert.AreEqual(2, resumes);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(RoutineStatus.Completed, handle.Status);
    }

    [TestMethod]
    public void AStepThatLandsOnATruePredicateStillResumesExactlyOnce()
    {
        var harness = new SchedulerHarness().Load();
        var resumes = 0;

        IEnumerator<Wait> Routine()
        {
            resumes++;
            yield return Wait.Until(() => true);
            resumes++;
            yield return Wait.NextFrame;
            resumes++;
        }

        var handle = harness.Scene.Start(Routine());

        handle.Step();

        Assert.AreEqual(
            1,
            resumes,
            "Step resumes once by contract, so the already-true wait it adopted is left for the pass");

        harness.Tick();

        Assert.AreEqual(2, resumes, "which releases it in the next pass");
    }

    [TestMethod]
    public void APredicateThatThrowsFaultsTheRoutineRunsFinallyAndRethrows()
    {
        var harness = new SchedulerHarness().Load();
        var log = new List<string>();

        IEnumerator<Wait> Routine()
        {
            try
            {
                yield return Wait.Until(() => throw new InvalidOperationException("predicate fault"));
            }
            finally
            {
                log.Add("finally");
            }
        }

        var handle = harness.Scene.Start(Routine());
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Tick());

        Assert.AreEqual("predicate fault", failure.Message, "the driver sees the author's exception, unwrapped");
        Assert.AreEqual(RoutineStatus.Faulted, handle.Status);
        CollectionAssert.AreEqual(new[] { "finally" }, log, "cleanup runs exactly once");
    }

    [TestMethod]
    public void AWaiterOnAConditionRoutineSeesItFinishAPassLater()
    {
        // The rejoin cost is a property of joining a routine, not of the wait the child used; this
        // pins that Until does not somehow escape it when it is the thing behind the helper.
        var harness = new SchedulerHarness().Load();
        var settled = false;
        harness.Scene.UpdateAction = _ => settled = harness.Frame >= SettlesAt;

        var landed = -1L;

        IEnumerator<Wait> Helper()
        {
            yield return Wait.Until(() => settled);
        }

        IEnumerator<Wait> Parent()
        {
            yield return Wait.For(Helper());
            landed = harness.Frame;
        }

        harness.Scene.Start(Parent());
        harness.Tick(6);

        Assert.AreEqual(SettlesAt + 1L, landed, "one pass per level of nesting, whatever the child waited on");
    }

    [TestMethod]
    public void ChainedConditionsQuantizeToOnePassEach()
    {
        var harness = new SchedulerHarness().Load();
        var stage = 0;
        var landed = -1L;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.Until(() => stage >= 1);
            yield return Wait.Until(() => stage >= 2);
            landed = harness.Frame;
        }

        // Both conditions come true at once, so the second is already satisfied in the pass the
        // first releases in.
        harness.Scene.UpdateAction = _ => stage = harness.Frame >= 1L ? 2 : 0;
        harness.Scene.Start(Routine());
        harness.Tick(4);

        Assert.AreEqual(
            1L,
            landed,
            "the second condition is evaluated in the same pass the first released in, so a "
            + "condition already true when its turn comes costs no frame");
    }
}
