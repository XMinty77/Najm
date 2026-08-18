namespace Najm.Core;

/// <summary>One scheduled coroutine: its enumerator, its owner, and its current wait.</summary>
/// <remarks>
/// The public face of this is <see cref="CoroutineHandle"/>, which is a struct wrapping one of
/// these. Keeping the state in a class and the handle in a struct means starting a routine
/// allocates exactly once and handing the handle around afterwards allocates nothing.
/// </remarks>
internal sealed class Routine : IScheduled
{
    private readonly Scheduler scheduler;
    private readonly IEnumerator<Wait> enumerator;
    private readonly Node? owner;
    private Wait current;
    private double accumulated;
    private bool started;
    private bool resuming;
    private bool disposeDeferred;

    internal Routine(Scheduler scheduler, IEnumerator<Wait> enumerator, Node? owner)
    {
        this.scheduler = scheduler;
        this.enumerator = enumerator;
        this.owner = owner;
    }

    /// <summary>Gets the owning node, or null for a scene-owned routine.</summary>
    internal Node? Owner => owner;

    /// <inheritdoc />
    public RoutineStatus Status { get; private set; } = RoutineStatus.Running;

    /// <summary>Gets or sets whether the author has paused this routine.</summary>
    internal bool Paused { get; set; }

    /// <summary>Gets whether this routine is eligible to be polled by the coroutine pass.</summary>
    internal bool IsEligible =>
        Status == RoutineStatus.Running && !Paused && Scheduler.OwnerIsEligible(owner);

    /// <summary>
    /// Evaluates the current wait against one pass and resumes the routine if it is satisfied.
    /// </summary>
    /// <param name="dt">Simulation seconds this tick consumed.</param>
    internal void Poll(double dt)
    {
        if (!started)
        {
            Advance();
            return;
        }

        switch (current.Kind)
        {
            case WaitKind.NextFrame:
                Advance();
                return;
            case WaitKind.Seconds:
                accumulated += dt;
                if (SimTime.Reached(accumulated, current.SecondsValue))
                {
                    Advance();
                }

                return;
            case WaitKind.ForRoutine:
                if (((Routine)current.Target!).Status != RoutineStatus.Running)
                {
                    Advance();
                }

                return;
            case WaitKind.ForAnimation:
                if (((Animation)current.Target!).Status != RoutineStatus.Running)
                {
                    Advance();
                }

                return;
            default:
                throw new InvalidOperationException($"Unknown wait kind '{current.Kind}'.");
        }
    }

    /// <summary>Ends the routine now, disposing the enumerator at this call site.</summary>
    /// <remarks>
    /// Disposal here is the contract that makes author <c>try/finally</c> cleanup deterministic. The
    /// one exception is a routine cancelling itself from inside its own body: disposing an
    /// enumerator that is mid-<c>MoveNext</c> would run its finally blocks twice, so that disposal is
    /// deferred to the moment the resume returns, which is still before any other routine runs.
    /// </remarks>
    internal void Cancel()
    {
        if (Status != RoutineStatus.Running)
        {
            return;
        }

        Status = RoutineStatus.Cancelled;
        if (resuming)
        {
            disposeDeferred = true;
            return;
        }

        enumerator.Dispose();
    }

    /// <summary>Fast-forwards the current wait and resumes exactly once.</summary>
    /// <returns>False if the routine was already terminal.</returns>
    internal bool Step()
    {
        if (Status != RoutineStatus.Running)
        {
            return false;
        }
        if (resuming)
        {
            throw new InvalidOperationException("A routine cannot be stepped from inside its own body.");
        }

        if (started && current.Kind == WaitKind.ForAnimation)
        {
            ((Animation)current.Target!).Complete();
        }

        Advance();
        return true;
    }

    /// <summary>Runs the enumerator to its next yield, adopting whatever wait it produced.</summary>
    private void Advance()
    {
        bool moved;
        resuming = true;
        try
        {
            moved = enumerator.MoveNext();
        }
        catch
        {
            Status = RoutineStatus.Faulted;
            enumerator.Dispose();
            throw;
        }
        finally
        {
            resuming = false;
        }

        started = true;
        if (disposeDeferred)
        {
            disposeDeferred = false;
            enumerator.Dispose();
            return;
        }
        if (!moved)
        {
            Status = RoutineStatus.Completed;
            enumerator.Dispose();
            return;
        }

        var next = enumerator.Current;
        if (next.Kind == WaitKind.StartRoutine)
        {
            next = Wait.For(scheduler.Start((IEnumerator<Wait>)next.Target!, owner));
        }

        current = next;
        accumulated = 0d;
    }
}
