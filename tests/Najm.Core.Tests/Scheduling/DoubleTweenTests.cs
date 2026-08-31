using Najm.Utils;

namespace Najm.Core.Tests.Scheduling;

/// <summary>
/// Covers the <see cref="double"/> overloads of <c>Animate</c>: same pass, same timing, endpoints
/// that survive.
/// </summary>
/// <remarks>
/// The float overload's own contract is pinned in <see cref="TweenPassTests"/> and is not repeated
/// here. What these add is the reason the second overload exists — a scene's quantities are doubles,
/// and routing them through a float rounds the endpoints before the tween has run — plus the
/// overload-resolution rule that decides which of the two a call site actually reached, since a
/// float setter widens into a double field without complaint.
/// </remarks>
[TestClass]
public sealed class DoubleTweenTests
{
    /// <summary>A double whose nearest float is a different number, which is the whole point.</summary>
    private const double NotAFloat = 0.1d;

    [TestMethod]
    public void TheEndpointsAreCarriedExactlyWhereAFloatWouldRoundThem()
    {
        var harness = new SchedulerHarness().Load();
        var written = double.NaN;

        var handle = harness.Scene.Animate(v => written = v, from: NotAFloat, to: 155.1d, duration: 0.5d);

        Assert.AreEqual(NotAFloat, written, "the from-value is applied at the call site, unrounded");
        Assert.AreNotEqual(
            (double)(float)NotAFloat,
            written,
            "and it is not the float the other overload would have carried");

        harness.Tick(30);

        Assert.AreEqual(RoutineStatus.Completed, handle.Status, "0.5 s at 60 fps is exactly 30 ticks");
        Assert.AreEqual(155.1d, written, "the final write is the exact to-value");
    }

    [TestMethod]
    public void EndpointsOutsideFloatRangeSurviveTheWholeRamp()
    {
        // A float tween over these would write infinities from its first delta; this is the sharpest
        // available statement that the arithmetic is double all the way through.
        var harness = new SchedulerHarness().Load();
        var written = double.NaN;

        harness.Scene.Animate(v => written = v, from: 0d, to: 1e300d, duration: 0.5d);
        harness.Tick(15);

        Assert.IsTrue(double.IsFinite(written), $"a mid-ramp value must stay finite; it was {written}");
        Assert.IsTrue(written > 0d && written < 1e300d, "and inside the interval");

        harness.Tick(15);

        Assert.AreEqual(1e300d, written);
    }

    [TestMethod]
    public void TheEndpointLiteralsDecideWhichOverloadACallSiteGets()
    {
        // v.GetType() reports the compile-time delegate's parameter type through the box, which is
        // the only way to observe from a test which overload the compiler chose. The rule is
        // documented on Scene.Animate because a float setter widens into a double field in silence.
        var harness = new SchedulerHarness().Load();
        Type? fromIntLiterals = null;
        Type? fromDoubleLiterals = null;
        Type? fromFloatLiterals = null;

        harness.Scene.Animate(v => fromIntLiterals = v.GetType(), 0, 1, 0.5d);
        harness.Scene.Animate(v => fromDoubleLiterals = v.GetType(), 0d, 1d, 0.5d);
        harness.Scene.Animate(v => fromFloatLiterals = v.GetType(), 0f, 1f, 0.5d);

        Assert.AreEqual(
            typeof(float),
            fromIntLiterals,
            "an int literal converts to float in preference to double, so bare 0 and 1 reach the "
            + "float overload even when the target property is a double");
        Assert.AreEqual(typeof(double), fromDoubleLiterals, "suffixed endpoints reach the double overload");
        Assert.AreEqual(typeof(float), fromFloatLiterals);
    }

    [TestMethod]
    public void ADoubleTweenUsesTheEasingCurveItWasGiven()
    {
        var harness = new SchedulerHarness().Load();
        var written = double.NaN;

        harness.Scene.Animate(v => written = v, from: 0d, to: 10d, duration: 1d, Ease.InOutCubic);
        harness.Tick(30);

        // Half a second into a one-second ramp: the curve's own midpoint, evaluated in single
        // precision by contract and applied to double endpoints.
        Assert.AreEqual(10d * Ease.InOutCubic.Evaluate(0.5f), written, 1e-12d);
    }

    [TestMethod]
    public void ADoubleTweenAcceptsACustomTimingFunction()
    {
        var harness = new SchedulerHarness().Load();
        var written = double.NaN;

        harness.Scene.Animate(v => written = v, from: 0d, to: 4d, duration: 1d, new HalfSpeed());
        harness.Tick(30);

        Assert.AreEqual(1d, written, 1e-12d, "the custom curve returns half of its progress");
    }

    [TestMethod]
    public void ChainedDoubleTweensJoinedByWaitForOccupyExactlySixtyTicks()
    {
        var harness = new SchedulerHarness().Load();
        var written = double.NaN;
        var finished = -1L;

        IEnumerator<Wait> Routine()
        {
            yield return Wait.For(harness.Scene.Animate(v => written = v, from: 0d, to: 1d, duration: 0.5d));
            yield return Wait.For(harness.Scene.Animate(v => written = v, from: 1d, to: 2d, duration: 0.5d));
            finished = harness.Frame;
        }

        harness.Scene.Start(Routine());
        harness.Tick(61);

        Assert.AreEqual(2d, written, "and the chain lands exactly on the second ramp's to-value");
        Assert.AreEqual(
            60L,
            finished,
            "both ramps are created inside a pass whose tween pass has already run, so they consume "
            + "ticks 1-30 and 31-60: exactly 60 ticks for 0.5 s + 0.5 s, not 61");
    }

    [TestMethod]
    public void ANodeOwnedDoubleTweenIsCancelledAtDetachAndStopsAtItsCurrentValue()
    {
        var harness = new SchedulerHarness().Load();
        var node = harness.Root.Add(new Node2D());
        var written = double.NaN;

        var handle = node.Animate(v => written = v, from: 0d, to: 100d, duration: 1d);
        harness.Tick(30);

        var atDetach = written;
        Assert.IsTrue(harness.Root.Remove(node));
        harness.Tick();

        Assert.AreEqual(RoutineStatus.Cancelled, handle.Status);
        Assert.AreEqual(atDetach, written, "cancellation stops at the current value rather than snapping");
    }

    [TestMethod]
    public void CompleteAndCancelBehaveAsTheyDoForAFloatTween()
    {
        var harness = new SchedulerHarness().Load();
        var writes = 0;
        var written = double.NaN;

        var handle = harness.Scene.Animate(
            v =>
            {
                writes++;
                written = v;
            },
            from: 0d,
            to: 7.5d,
            duration: 10d);

        Assert.AreEqual(1, writes, "the from-value write");

        handle.Complete();

        Assert.AreEqual(2, writes, "one further write");
        Assert.AreEqual(7.5d, written, "with the exact final value");
        Assert.AreEqual(RoutineStatus.Completed, handle.Status);
    }

    [TestMethod]
    public void TheDoubleOverloadsValidateTheirArguments()
    {
        var harness = new SchedulerHarness().Load();
        var scene = harness.Scene;
        var node = harness.Root.Add(new Node2D());

        Assert.ThrowsExactly<ArgumentNullException>(
            () => scene.Animate((Action<double>)null!, 0d, 1d, 1d));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => scene.Animate(_ => { }, 0d, 1d, 1d, (ITimingFunction)null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Animate(_ => { }, double.NaN, 1d, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Animate(_ => { }, 0d, double.PositiveInfinity, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => scene.Animate(_ => { }, 0d, 1d, -0.001d));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => node.Animate((Action<double>)null!, 0d, 1d, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => node.Animate(_ => { }, 0d, 1d, double.NaN));
    }

    /// <summary>A curve that returns half its progress, so a tween's value is checkable by hand.</summary>
    private sealed class HalfSpeed : ITimingFunction
    {
        public float Evaluate(float progress) => progress * 0.5f;
    }
}
