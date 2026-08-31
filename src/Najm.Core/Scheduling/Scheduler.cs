using System.Diagnostics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Owns one scene's live coroutines and tweens and runs their two per-frame passes.</summary>
/// <remarks>
/// <para>
/// Both passes live inside the Update phase, after the whole tree has updated and before the
/// end-of-update flush, and the <strong>tween pass runs first</strong>. That order is what makes
/// <c>Wait.For(animation)</c> chain without a blank frame: a tween that reaches its end at frame N
/// is already terminal when the coroutine pass evaluates its waiters in the same frame, so two
/// 0.5 s tweens joined by a wait occupy exactly 60 ticks at 60 fps rather than 61.
/// </para>
/// <para>
/// The coroutine pass is a FIFO drain to empty. Routines started before the pass are already queued
/// and get their first resume this frame; routines started during it are appended and resumed later
/// in the same pass. Warm passes allocate nothing: both queues are index-walked lists, waits are
/// structs, and the terminal-entry compaction runs in place.
/// </para>
/// </remarks>
internal sealed class Scheduler
{
    private readonly Scene scene;
    private readonly List<Routine> routines = [];
    private readonly List<Animation> animations = [];
    private bool draining;

    internal Scheduler(Scene scene) => this.scene = scene;

    /// <summary>Gets the number of routines the scheduler is holding, terminal ones included.</summary>
    internal int RoutineCount => routines.Count;

    /// <summary>Gets the number of animations the scheduler is holding, terminal ones included.</summary>
    internal int AnimationCount => animations.Count;

    /// <summary>Gets whether any routine or tween has not yet reached a terminal status.</summary>
    /// <remarks>
    /// Scanned rather than read off the counts, because a terminal entry stays in its queue until
    /// the compaction at the end of the pass that ended it, and because a caller asking this
    /// question wants the live answer, not the queue's length. Paused work and work under a
    /// disabled owner is still live: it has not finished, it has only stopped running.
    /// </remarks>
    internal bool HasLiveWork
    {
        get
        {
            for (var index = 0; index < routines.Count; index++)
            {
                if (routines[index].Status == RoutineStatus.Running)
                {
                    return true;
                }
            }
            for (var index = 0; index < animations.Count; index++)
            {
                if (animations[index].Status == RoutineStatus.Running)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Returns whether a routine or tween owned by <paramref name="owner"/> may run: every node from
    /// the owner up to its root must be enabled.
    /// </summary>
    /// <remarks>
    /// Disabling a node is exactly Pause semantics for everything the subtree owns — the wait is not
    /// evaluated, <c>Seconds</c> stops accumulating, tween time freezes — and re-enabling resumes in
    /// place. A scene-owned routine has no owner and is always eligible.
    /// </remarks>
    internal static bool OwnerIsEligible(Node? owner)
    {
        for (var node = owner; node is not null; node = node.Parent)
        {
            if (!node.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Queues a routine and returns its handle. The routine's first resume is a later pass event.</summary>
    /// <param name="routine">The enumerator to drive.</param>
    /// <param name="owner">The owning node, or null for scene lifetime.</param>
    internal CoroutineHandle Start(IEnumerator<Wait> routine, Node? owner)
    {
        AssertStartablePhase();
        var record = new Routine(this, routine, owner);
        routines.Add(record);
        return new CoroutineHandle(record);
    }

    /// <summary>Applies the from-value at this call site, queues the tween, and returns its handle.</summary>
    /// <param name="setter">Receives every value the tween produces.</param>
    /// <param name="from">The value written now.</param>
    /// <param name="to">The value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="builtIn">The built-in easing curve, used when <paramref name="custom"/> is null.</param>
    /// <param name="custom">A custom easing curve, or null to use <paramref name="builtIn"/>.</param>
    /// <param name="owner">The owning node, or null for scene lifetime.</param>
    internal AnimationHandle Animate(
        Action<float> setter,
        float from,
        float to,
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
    {
        ArgumentNullException.ThrowIfNull(setter);
        if (!float.IsFinite(from))
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "A tween's from-value must be finite.");
        }
        if (!float.IsFinite(to))
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "A tween's to-value must be finite.");
        }

        ValidateDuration(duration);
        return Queue(new FloatAnimation(setter, from, to, duration, builtIn, custom, owner));
    }

    /// <inheritdoc cref="Animate(Action{float}, float, float, double, TimingFunction, ITimingFunction, Node)" />
    internal AnimationHandle Animate(
        Action<double> setter,
        double from,
        double to,
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
    {
        ArgumentNullException.ThrowIfNull(setter);
        if (!double.IsFinite(from))
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "A tween's from-value must be finite.");
        }
        if (!double.IsFinite(to))
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "A tween's to-value must be finite.");
        }

        ValidateDuration(duration);
        return Queue(new DoubleAnimation(setter, from, to, duration, builtIn, custom, owner));
    }

    private static void ValidateDuration(double duration)
    {
        if (!double.IsFinite(duration) || duration < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "A tween duration must be finite and non-negative.");
        }
    }

    /// <summary>Applies the from-value at the call site, then enqueues the tween.</summary>
    /// <remarks>
    /// The order matters and is the contract: the property never shows a frame of its old value, and
    /// a from-value write that throws leaves nothing queued behind it.
    /// </remarks>
    private AnimationHandle Queue(Animation record)
    {
        AssertStartablePhase();
        record.ApplyFromValue();
        animations.Add(record);
        return new AnimationHandle(record);
    }

    /// <summary>Advances every eligible tween by this tick's simulation delta.</summary>
    /// <remarks>
    /// The live count is snapshotted first, so a tween started by a setter during this pass gets its
    /// first delta at the next one — the same rule that applies to a tween started anywhere else in
    /// the frame.
    /// </remarks>
    internal void RunTweenPass(in TickContext tick)
    {
        var dt = tick.Time.Dt;
        var count = animations.Count;
        for (var index = 0; index < count; index++)
        {
            var animation = animations[index];
            if (animation.IsEligible)
            {
                animation.Advance(dt);
            }
        }

        Compact(animations);
    }

    /// <summary>Drains the routine queue to empty, in enqueue order, once.</summary>
    internal void RunCoroutinePass(in TickContext tick)
    {
        if (draining)
        {
            throw new InvalidOperationException("The coroutine pass is not reentrant.");
        }

        var dt = tick.Time.Dt;
        draining = true;
        try
        {
            // Count is re-read every iteration on purpose: a routine started during the pass is
            // appended here and must be resumed later in this same pass.
            for (var index = 0; index < routines.Count; index++)
            {
                var routine = routines[index];
                if (routine.IsEligible)
                {
                    routine.Poll(dt);
                }
            }
        }
        finally
        {
            draining = false;
            Compact(routines);
        }
    }

    /// <summary>
    /// Cancels every routine and tween owned by a node in the subtree rooted at
    /// <paramref name="root"/>, collecting rather than throwing any failure an author's cleanup
    /// raises.
    /// </summary>
    /// <remarks>
    /// This runs during the deferred flush that detaches the subtree, while parent links are still
    /// intact, which is what lets ownership be tested by walking ancestors.
    /// </remarks>
    internal void CancelOwnedBySubtree(Node root, List<Exception> failures)
    {
        for (var index = 0; index < routines.Count; index++)
        {
            var routine = routines[index];
            if (routine.Status == RoutineStatus.Running && IsWithin(routine.Owner, root))
            {
                TryCancel(routine, failures);
            }
        }
        for (var index = 0; index < animations.Count; index++)
        {
            var animation = animations[index];
            if (animation.Status == RoutineStatus.Running && IsWithin(animation.Owner, root))
            {
                animation.Cancel();
            }
        }

        CompactIfIdle();
    }

    /// <summary>Cancels everything the scheduler holds, collecting author cleanup failures.</summary>
    internal void CancelAll(List<Exception> failures)
    {
        for (var index = 0; index < routines.Count; index++)
        {
            TryCancel(routines[index], failures);
        }
        for (var index = 0; index < animations.Count; index++)
        {
            animations[index].Cancel();
        }

        CompactIfIdle();
    }

    private static bool IsWithin(Node? owner, Node root)
    {
        for (var node = owner; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, root))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryCancel(Routine routine, List<Exception> failures)
    {
        try
        {
            routine.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void CompactIfIdle()
    {
        if (draining)
        {
            return;
        }

        Compact(routines);
        Compact(animations);
    }

    /// <summary>Drops terminal entries while preserving enqueue order, without allocating.</summary>
    /// <remarks>
    /// Order is the scheduler's contract, so this is a stable in-place compaction rather than the
    /// cheaper swap-with-last. The class constraint keeps the status read an interface call on a
    /// reference; nothing is boxed.
    /// </remarks>
    private static void Compact<T>(List<T> list)
        where T : class, IScheduled
    {
        var write = 0;
        for (var read = 0; read < list.Count; read++)
        {
            var item = list[read];
            if (item.Status != RoutineStatus.Running)
            {
                continue;
            }

            if (write != read)
            {
                list[write] = item;
            }

            write++;
        }

        if (write != list.Count)
        {
            list.RemoveRange(write, list.Count - write);
        }
    }

    /// <summary>
    /// Refuses a start from a phase where scheduling is a contract violation. Debug-only by
    /// specification.
    /// </summary>
    /// <remarks>
    /// Starting from <see cref="Scene.Render(IRenderTarget)"/> or
    /// <see cref="Scene.RenderDirect(IDrawContext2D)"/> would break render idempotence, because the
    /// engine may render one ticked frame more than once and each render would queue the routine
    /// again. The Layout phase is the other illegal caller; it does not exist yet, so there is
    /// nothing here to test for it.
    /// </remarks>
    [Conditional("DEBUG")]
    private void AssertStartablePhase()
    {
        if (scene.IsRendering)
        {
            throw new InvalidOperationException(
                "Starting a coroutine or an animation from Render violates render idempotence; "
                + "start it from an input handler or Update instead.");
        }
    }
}

/// <summary>What the scheduler needs from anything it queues: a lifetime status it can compact by.</summary>
internal interface IScheduled
{
    /// <summary>Gets the entry's lifetime status.</summary>
    RoutineStatus Status { get; }
}

/// <summary>Comparisons for simulation time accumulated one tick at a time.</summary>
internal static class SimTime
{
    /// <summary>
    /// The slack allowed when deciding that accumulated time has reached a target, as a fraction of
    /// the target with a floor of one nanosecond.
    /// </summary>
    private const double Slack = 1e-9;

    /// <summary>
    /// Returns whether <paramref name="accumulated"/> has reached <paramref name="target"/>, allowing
    /// for the rounding a fixed-step delta carries.
    /// </summary>
    /// <remarks>
    /// The slack is not cosmetic. A 60 fps delta is the double nearest 1/60, which is slightly
    /// <em>below</em> the exact value, so thirty of them sum to 0.49999999999999994 — under 0.5 even
    /// with perfectly compensated addition, because the addends themselves are short. A bare
    /// <c>&gt;=</c> would therefore make <c>Seconds(0.5)</c> take 31 ticks and quietly break the
    /// normative "exactly 30 ticks" rule and the gap-free chaining that depends on it. One part in
    /// 1e9 is roughly a nanosecond per second: far above the accumulated rounding of any realistic
    /// horizon, and far below one tick of any realistic frame rate.
    /// </remarks>
    internal static bool Reached(double accumulated, double target) =>
        accumulated >= target - (Slack * Math.Max(1d, target));
}
