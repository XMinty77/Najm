namespace Najm.Core;

/// <summary>Describes what a suspended coroutine is waiting for.</summary>
/// <remarks>
/// <para>
/// This is a value type and the scheduler never boxes it: a routine is an
/// <see cref="IEnumerator{T}"/> of <see cref="Wait"/>, so each <c>yield return</c> hands the
/// scheduler a copy of this struct and nothing reaches the heap on a warm frame.
/// </para>
/// <para>
/// The M1 vocabulary is <see cref="NextFrame"/>, <see cref="Seconds(double)"/>,
/// <see cref="Until(Func{bool})"/> and the three <c>For</c> overloads. <c>Frames</c>,
/// <c>Never</c>, <c>All</c>, <c>Any</c> and <c>Signal</c> are specified but not yet built.
/// </para>
/// </remarks>
public readonly struct Wait : IEquatable<Wait>
{
    private readonly WaitKind kind;
    private readonly double seconds;
    private readonly object? target;

    private Wait(WaitKind kind, double seconds, object? target)
    {
        this.kind = kind;
        this.seconds = seconds;
        this.target = target;
    }

    /// <summary>Gets the wait that resumes in the next frame's coroutine pass.</summary>
    /// <remarks>
    /// This is the zero-initialized value, so <c>default(Wait)</c> and <see cref="NextFrame"/> are
    /// the same wait and a defaulted <see cref="Wait"/> is never a broken one.
    /// </remarks>
    public static Wait NextFrame => default;

    /// <summary>
    /// Creates a wait that accumulates <see cref="TimeInfo.Dt"/> at each eligible coroutine pass and
    /// releases on the first pass where the accumulation reaches <paramref name="seconds"/>.
    /// </summary>
    /// <param name="seconds">Finite, non-negative simulation seconds to wait.</param>
    /// <remarks>
    /// Accumulation starts at zero for each wait and <strong>no fractional remainder carries</strong>
    /// into the next one, so chained waits quantize per-wait to the tick grid: at a fixed 60 fps,
    /// <c>Seconds(0.5)</c> is exactly 30 ticks and two of them are exactly 60. Simulation time is the
    /// only clock involved; there is no wall-clock path.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seconds"/> is not finite and non-negative.
    /// </exception>
    public static Wait Seconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                "A Seconds wait must be finite and non-negative.");
        }

        return new Wait(WaitKind.Seconds, seconds, null);
    }

    /// <summary>
    /// Creates a wait that releases on the first pass where <paramref name="predicate"/> is true —
    /// starting with the pass this wait is yielded in.
    /// </summary>
    /// <param name="predicate">
    /// A pure read of scene state. It is called once per eligible pass, inside the coroutine pass,
    /// so it observes the frame after the whole tree has updated.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>This is the inline spin, with its timing preserved exactly.</strong>
    /// <c>yield return Wait.Until(() =&gt; c);</c> and <c>while (!c) yield return Wait.NextFrame;</c>
    /// resume the routine in the same pass as one another in every case, and that equivalence is
    /// the reason the predicate is evaluated at the moment the wait is yielded rather than first at
    /// the following pass. A spin tests its condition <em>before</em> it suspends, so a condition
    /// that already holds costs it no frame at all; a wait that suspended first and asked afterwards
    /// would cost one, and replacing a spin with it would silently retime the routine. So: the
    /// predicate is evaluated during the pass that yields the wait, and once per eligible pass after
    /// that, and the routine resumes in the pass where it first returns true.
    /// </para>
    /// <para>
    /// <strong>Prefer this to wrapping a spin in a helper routine.</strong> Hoisting a spin into a
    /// helper and joining it with <see cref="For(IEnumerator{Wait})"/> reads as the same thing and
    /// is one frame later, once per level of nesting — see that overload's remarks for why. This
    /// wait introduces no nesting level, so it costs nothing to move behind a method name, which is
    /// the property a helper is supposed to have.
    /// </para>
    /// <para>
    /// <strong>The predicate is a read, not a step.</strong> It runs inside the coroutine pass,
    /// which is not reentrant, so it must not start or step routines, and it should not mutate the
    /// scene: it is called an unspecified number of times — never for a paused routine or one under
    /// a disabled owner, and not at all by <see cref="CoroutineHandle.Step"/>, which deems the wait
    /// satisfied. Nothing enforces purity; this is a contract. A predicate that throws faults its
    /// routine exactly as a throw from the routine's own body does, running the author's
    /// <c>finally</c> blocks and propagating out of the tick.
    /// </para>
    /// <para>
    /// <strong>There is no timeout.</strong> A predicate that never becomes true parks the routine
    /// until something cancels it, which is <c>Wait.Never</c>'s behaviour arrived at by accident.
    /// And an already-true predicate resumes the routine in the same pass, so a loop whose body
    /// yields nothing but already-true <c>Until</c> waits never returns control to the pass — the
    /// same author bug as a <c>while (true)</c> body with no <c>yield</c> in it, reached a less
    /// obvious way.
    /// </para>
    /// <para>
    /// The delegate is held for as long as the wait is, so a closure over a node keeps that node
    /// alive for the life of the routine.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
    public static Wait Until(Func<bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new Wait(WaitKind.Until, 0d, predicate);
    }

    /// <summary>Creates a wait that releases when <paramref name="routine"/> reaches any terminal status.</summary>
    /// <param name="routine">The routine to join.</param>
    /// <remarks>
    /// <para>
    /// Completion, cancellation and faulting all release the waiter, which resumes normally and may
    /// read <see cref="CoroutineHandle.Status"/> to find out which happened.
    /// </para>
    /// <para>
    /// <strong>The release is one pass after the child's last one.</strong> The pass drains its
    /// queue once per tick in enqueue order and this join is tested when the <em>waiter</em> is
    /// polled, so a child that reaches a terminal status at its own position in the queue is not
    /// observed by anything already polled this pass — which includes every routine that started it,
    /// since a starter is always enqueued ahead of what it starts. Joining a routine therefore costs
    /// one pass at the rejoin. See <see cref="For(IEnumerator{Wait})"/> for what that means for
    /// helper routines, and note that <see cref="For(AnimationHandle)"/> does <em>not</em> pay it:
    /// the tween pass runs to completion before the coroutine pass begins.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="routine"/> is the default handle.</exception>
    public static Wait For(CoroutineHandle routine)
    {
        var target = routine.Target
            ?? throw new ArgumentException("The default CoroutineHandle refers to no routine.", nameof(routine));
        return new Wait(WaitKind.ForRoutine, 0d, target);
    }

    /// <summary>Creates a wait that releases when <paramref name="animation"/> reaches any terminal status.</summary>
    /// <param name="animation">The animation to join.</param>
    /// <remarks>
    /// The tween pass runs immediately before the coroutine pass, so an animation that reaches its
    /// end at frame N releases its waiters in that same frame's pass and chained animations are
    /// gap-free.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="animation"/> is the default handle.</exception>
    public static Wait For(AnimationHandle animation)
    {
        var target = animation.Target
            ?? throw new ArgumentException("The default AnimationHandle refers to no animation.", nameof(animation));
        return new Wait(WaitKind.ForAnimation, 0d, target);
    }

    /// <summary>
    /// Creates a wait that starts <paramref name="routine"/> as a child of the waiting routine — same
    /// owner, same lifetime rules — and then joins it.
    /// </summary>
    /// <param name="routine">The child routine to start.</param>
    /// <remarks>
    /// <para>
    /// The child is started at the moment the parent yields this wait, which is inside the coroutine
    /// pass, so it is appended to the queue and gets its first resume later in that same pass. Nested
    /// choreography therefore composes without a one-frame hiccup <em>on entry</em>.
    /// </para>
    /// <para>
    /// <strong>The exit costs one pass per level of nesting, and that is not symmetric with the
    /// entry.</strong> The queue drains once per tick in enqueue order; the parent sits ahead of the
    /// child it started, and its join is evaluated when the parent is polled. So the child's
    /// completion — which happens later in the pass, behind the parent — is not seen until the
    /// <em>next</em> pass. Two levels of nesting cost two passes, and so on down the chain.
    /// </para>
    /// <para>
    /// <strong>The consequence is that moving a wait into a helper routine retimes it.</strong>
    /// <c>while (!c) yield return Wait.NextFrame;</c> resumes in the pass where <c>c</c> first
    /// holds; the identical spin hoisted into a helper and joined with this overload resumes one
    /// pass later, and nothing at the call site says so. For a condition, use
    /// <see cref="Until(Func{bool})"/>, which introduces no nesting level and therefore costs
    /// nothing to name. This overload is for a <em>beat</em> — a stretch of choreography whose
    /// length is its own business — where one extra pass at the seam is not observable and the
    /// factoring is worth having.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="routine"/> is null.</exception>
    public static Wait For(IEnumerator<Wait> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return new Wait(WaitKind.StartRoutine, 0d, routine);
    }

    /// <inheritdoc />
    public bool Equals(Wait other) =>
        kind == other.kind &&
        seconds.Equals(other.seconds) &&
        ReferenceEquals(target, other.target);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Wait other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(kind, seconds, target is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target));

    /// <summary>Tests two waits for equality.</summary>
    public static bool operator ==(Wait left, Wait right) => left.Equals(right);

    /// <summary>Tests two waits for inequality.</summary>
    public static bool operator !=(Wait left, Wait right) => !left.Equals(right);

    internal WaitKind Kind => kind;

    internal double SecondsValue => seconds;

    internal object? Target => target;
}

/// <summary>The kinds of wait the M1 scheduler understands.</summary>
internal enum WaitKind : byte
{
    /// <summary>Resume in the next frame's pass. Zero so that the default wait is this one.</summary>
    NextFrame,

    /// <summary>Resume once accumulated eligible simulation time reaches the requested seconds.</summary>
    Seconds,

    /// <summary>
    /// Resume on the first eligible pass whose predicate evaluation returns true, the pass that
    /// yielded the wait included.
    /// </summary>
    Until,

    /// <summary>Resume once the joined routine is terminal.</summary>
    ForRoutine,

    /// <summary>Resume once the joined animation is terminal.</summary>
    ForAnimation,

    /// <summary>
    /// Transitional: the scheduler starts the carried enumerator with the yielding routine's owner
    /// and rewrites the wait as <see cref="ForRoutine"/>. It is never stored as a routine's current
    /// wait.
    /// </summary>
    StartRoutine,
}
