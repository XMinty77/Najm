namespace Najm.Core;

/// <summary>Describes where a scheduled routine or animation is in its lifetime.</summary>
/// <remarks>
/// <see cref="Running"/> is the only non-terminal status. The three terminal statuses are distinct
/// because a <see cref="Wait.For(CoroutineHandle)"/> waiter releases on any of them and may branch
/// on which one it observed: a child's cancellation or fault never silently kills its parent.
/// </remarks>
public enum RoutineStatus : byte
{
    /// <summary>The routine or animation is live and will be polled by the scheduler.</summary>
    Running,

    /// <summary>The routine ran to completion, or the animation reached its end value.</summary>
    Completed,

    /// <summary>Cancellation ended it early, at its current value.</summary>
    Cancelled,

    /// <summary>A throw during resume ended it. The exception was rethrown to the driver.</summary>
    Faulted,
}
