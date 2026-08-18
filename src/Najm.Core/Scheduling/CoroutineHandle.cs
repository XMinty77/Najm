namespace Najm.Core;

/// <summary>Controls one scheduled coroutine.</summary>
/// <remarks>
/// A handle is a value type wrapping the scheduler's routine record, so it is free to copy, to hold
/// in a <see cref="Wait"/>, and to pass around a frame path. Two handles to the same routine compare
/// equal. The zero-initialized value refers to no routine and every member except
/// <see cref="IsValid"/> throws for it.
/// </remarks>
public readonly struct CoroutineHandle : IEquatable<CoroutineHandle>
{
    private readonly Routine? routine;

    internal CoroutineHandle(Routine routine) => this.routine = routine;

    /// <summary>Gets whether this handle refers to a routine.</summary>
    public bool IsValid => routine is not null;

    /// <summary>Gets the routine's lifetime status.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public RoutineStatus Status => Required.Status;

    /// <summary>Gets whether the routine has not yet reached a terminal status.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public bool IsRunning => Required.Status == RoutineStatus.Running;

    /// <summary>Removes the routine from eligibility, freezing its subjective time.</summary>
    /// <remarks>
    /// A paused routine's wait is not evaluated at all: a <see cref="Wait.Seconds(double)"/>
    /// accumulation stops where it is rather than continuing to count. <see cref="Resume"/> carries
    /// on from there. Pausing a terminal routine does nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Pause() => Required.Paused = true;

    /// <summary>Returns a paused routine to eligibility, in place.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Resume() => Required.Paused = false;

    /// <summary>Ends the routine immediately and synchronously.</summary>
    /// <remarks>
    /// The routine will never resume, its status becomes <see cref="RoutineStatus.Cancelled"/>, and
    /// <c>enumerator.Dispose</c> runs <strong>at this call site</strong> so an author's
    /// <c>try/finally</c> cleanup executes deterministically rather than at some later collection.
    /// Waiters joined to it release at their next evaluation. Cancelling a terminal routine does
    /// nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Cancel() => Required.Cancel();

    /// <summary>Fast-forwards the current wait, then resumes the routine exactly once.</summary>
    /// <returns>False if the routine was already terminal, true otherwise.</returns>
    /// <remarks>
    /// <para>
    /// This is the synchronous single-step for walkthroughs and debugging. It works on paused
    /// routines and leaves them paused, and it drives only this routine — no other routine is
    /// executed as a side effect. A routine that has never resumed performs its first resume.
    /// </para>
    /// <para>
    /// Fast-forwarding means: <see cref="Wait.NextFrame"/> and <see cref="Wait.Seconds(double)"/> are
    /// deemed satisfied with the sim clock untouched; a joined animation is
    /// <see cref="AnimationHandle.Complete"/>d; a joined routine's join is released without the child
    /// being force-run, so the child continues on its own schedule.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This is the default handle, or the routine is being stepped from inside its own body.
    /// </exception>
    public bool Step() => Required.Step();

    /// <inheritdoc />
    public bool Equals(CoroutineHandle other) => ReferenceEquals(routine, other.routine);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CoroutineHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        routine is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(routine);

    /// <summary>Tests whether two handles refer to the same routine.</summary>
    public static bool operator ==(CoroutineHandle left, CoroutineHandle right) => left.Equals(right);

    /// <summary>Tests whether two handles refer to different routines.</summary>
    public static bool operator !=(CoroutineHandle left, CoroutineHandle right) => !left.Equals(right);

    internal Routine? Target => routine;

    private Routine Required =>
        routine ?? throw new InvalidOperationException(
            "The default CoroutineHandle refers to no routine.");
}
