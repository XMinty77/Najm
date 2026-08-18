using Najm.Utils;

namespace Najm.Core;

/// <summary>One scheduled tween: a setter, an interval, a duration, and an easing curve.</summary>
/// <remarks>
/// Two easing fields rather than one keep the built-in curves off the heap. A
/// <see cref="TimingFunction"/> is a struct, so storing it as <see cref="ITimingFunction"/> would box
/// it once per <c>Animate</c> call; a custom implementation is already a reference and costs
/// nothing to hold. Evaluation allocates in neither case.
/// </remarks>
internal sealed class Animation : IScheduled
{
    private readonly Action<float> setter;
    private readonly float from;
    private readonly float to;
    private readonly double duration;
    private readonly TimingFunction builtIn;
    private readonly ITimingFunction? custom;
    private readonly Node? owner;
    private double elapsed;

    internal Animation(
        Action<float> setter,
        float from,
        float to,
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
    {
        this.setter = setter;
        this.from = from;
        this.to = to;
        this.duration = duration;
        this.builtIn = builtIn;
        this.custom = custom;
        this.owner = owner;
    }

    /// <summary>Gets the owning node, or null for a scene-owned tween.</summary>
    internal Node? Owner => owner;

    /// <inheritdoc />
    public RoutineStatus Status { get; private set; } = RoutineStatus.Running;

    /// <summary>Gets or sets whether the author has paused this tween.</summary>
    internal bool Paused { get; set; }

    /// <summary>Gets whether this tween is eligible to be advanced by the tween pass.</summary>
    internal bool IsEligible =>
        Status == RoutineStatus.Running && !Paused && Scheduler.OwnerIsEligible(owner);

    /// <summary>Applies the from-value. Runs synchronously at the <c>Animate</c> call site.</summary>
    internal void ApplyFromValue() => setter(from);

    /// <summary>Consumes one tick of simulation time and writes the resulting value.</summary>
    /// <param name="dt">Simulation seconds this tick consumed.</param>
    /// <remarks>
    /// The final write is the exact to-value rather than an eased approximation of it, so a tween
    /// always lands on its target. A zero duration therefore completes on its first pass.
    /// </remarks>
    internal void Advance(double dt)
    {
        elapsed += dt;
        if (SimTime.Reached(elapsed, duration))
        {
            elapsed = duration;
            Status = RoutineStatus.Completed;
            setter(to);
            return;
        }

        var progress = (float)(elapsed / duration);
        setter(from + ((to - from) * Evaluate(progress)));
    }

    /// <summary>Jumps to the end: the setter is invoked once with the final value.</summary>
    internal void Complete()
    {
        if (Status != RoutineStatus.Running)
        {
            return;
        }

        elapsed = duration;
        Status = RoutineStatus.Completed;
        setter(to);
    }

    /// <summary>Stops at the current value: no further setter call is made.</summary>
    internal void Cancel()
    {
        if (Status != RoutineStatus.Running)
        {
            return;
        }

        Status = RoutineStatus.Cancelled;
    }

    private float Evaluate(float progress) =>
        custom is null ? builtIn.Evaluate(progress) : custom.Evaluate(progress);
}
