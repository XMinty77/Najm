namespace Najm.Core;

/// <summary>Describes what a suspended coroutine is waiting for.</summary>
/// <remarks>
/// <para>
/// This is a value type and the scheduler never boxes it: a routine is an
/// <see cref="IEnumerator{T}"/> of <see cref="Wait"/>, so each <c>yield return</c> hands the
/// scheduler a copy of this struct and nothing reaches the heap on a warm frame.
/// </para>
/// <para>
/// The M1 vocabulary is <see cref="NextFrame"/>, <see cref="Seconds(double)"/> and the three
/// <c>For</c> overloads. <c>Frames</c>, <c>Until</c>, <c>Never</c>, <c>All</c>, <c>Any</c> and
/// <c>Signal</c> are specified but not yet built.
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

    /// <summary>Creates a wait that releases when <paramref name="routine"/> reaches any terminal status.</summary>
    /// <param name="routine">The routine to join.</param>
    /// <remarks>
    /// Completion, cancellation and faulting all release the waiter, which resumes normally and may
    /// read <see cref="CoroutineHandle.Status"/> to find out which happened.
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
    /// The child is started at the moment the parent yields this wait, which is inside the coroutine
    /// pass, so it is appended to the queue and gets its first resume later in that same pass. Nested
    /// choreography therefore composes without a one-frame hiccup.
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
