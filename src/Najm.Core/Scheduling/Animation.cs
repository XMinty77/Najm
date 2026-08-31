using Najm.Utils;

namespace Najm.Core;

/// <summary>One scheduled tween: an interval, a duration, and an easing curve.</summary>
/// <remarks>
/// <para>
/// Two easing fields rather than one keep the built-in curves off the heap. A
/// <see cref="TimingFunction"/> is a struct, so storing it as <see cref="ITimingFunction"/> would box
/// it once per <c>Animate</c> call; a custom implementation is already a reference and costs
/// nothing to hold. Evaluation allocates in neither case.
/// </para>
/// <para>
/// <b>The timing is here and the arithmetic is in the subclass.</b> Elapsed time, the reached-the-end
/// rule, the status, and the eligibility test are the same whatever a tween moves, so they live once
/// in this base; what differs between <see cref="FloatAnimation"/> and <see cref="DoubleAnimation"/>
/// is only the width of the endpoints and of the value written. Splitting it this way is what lets
/// the <c>double</c> overloads of <c>Animate</c> carry their endpoints exactly rather than through a
/// <c>float</c> that would round them at the call site.
/// </para>
/// </remarks>
internal abstract class Animation : IScheduled
{
    private readonly double duration;
    private readonly TimingFunction builtIn;
    private readonly ITimingFunction? custom;
    private readonly Node? owner;
    private double elapsed;

    private protected Animation(
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
    {
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
    internal abstract void ApplyFromValue();

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
            ApplyToValue();
            return;
        }

        var progress = (float)(elapsed / duration);
        ApplyEased(Evaluate(progress));
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
        ApplyToValue();
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

    /// <summary>Writes the exact to-value.</summary>
    private protected abstract void ApplyToValue();

    /// <summary>Writes the value this eased fraction of the way from the from-value to the to-value.</summary>
    /// <param name="eased">The easing curve's output, which may leave zero to one.</param>
    private protected abstract void ApplyEased(float eased);

    private float Evaluate(float progress) =>
        custom is null ? builtIn.Evaluate(progress) : custom.Evaluate(progress);
}

/// <summary>A tween over a <see cref="float"/> property.</summary>
internal sealed class FloatAnimation : Animation
{
    private readonly Action<float> setter;
    private readonly float from;
    private readonly float to;

    internal FloatAnimation(
        Action<float> setter,
        float from,
        float to,
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
        : base(duration, builtIn, custom, owner)
    {
        this.setter = setter;
        this.from = from;
        this.to = to;
    }

    internal override void ApplyFromValue() => setter(from);

    private protected override void ApplyToValue() => setter(to);

    private protected override void ApplyEased(float eased) => setter(from + ((to - from) * eased));
}

/// <summary>A tween over a <see cref="double"/> property.</summary>
/// <remarks>
/// The interpolation is done in double against exact double endpoints; only the easing curve is
/// single-precision, because <see cref="ITimingFunction"/> is a float contract. So the value written
/// carries the curve's resolution — about seven digits of the interval — around endpoints that are
/// exact, and the final write is the to-value itself.
/// </remarks>
internal sealed class DoubleAnimation : Animation
{
    private readonly Action<double> setter;
    private readonly double from;
    private readonly double to;

    internal DoubleAnimation(
        Action<double> setter,
        double from,
        double to,
        double duration,
        TimingFunction builtIn,
        ITimingFunction? custom,
        Node? owner)
        : base(duration, builtIn, custom, owner)
    {
        this.setter = setter;
        this.from = from;
        this.to = to;
    }

    internal override void ApplyFromValue() => setter(from);

    private protected override void ApplyToValue() => setter(to);

    private protected override void ApplyEased(float eased) => setter(from + ((to - from) * eased));
}
