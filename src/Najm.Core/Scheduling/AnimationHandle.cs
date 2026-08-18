namespace Najm.Core;

/// <summary>Controls one scheduled tween.</summary>
/// <remarks>
/// A handle is a value type wrapping the scheduler's animation record, so it is free to copy, to
/// hold in a <see cref="Wait"/>, and to pass around a frame path. Two handles to the same animation
/// compare equal. The zero-initialized value refers to no animation and every member except
/// <see cref="IsValid"/> throws for it.
/// </remarks>
public readonly struct AnimationHandle : IEquatable<AnimationHandle>
{
    private readonly Animation? animation;

    internal AnimationHandle(Animation animation) => this.animation = animation;

    /// <summary>Gets whether this handle refers to an animation.</summary>
    public bool IsValid => animation is not null;

    /// <summary>Gets the animation's lifetime status.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public RoutineStatus Status => Required.Status;

    /// <summary>Removes the animation from eligibility, freezing its tween time.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Pause() => Required.Paused = true;

    /// <summary>Returns a paused animation to eligibility, in place.</summary>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Resume() => Required.Paused = false;

    /// <summary>Stops the animation at its current value.</summary>
    /// <remarks>
    /// No further setter call is made, so whatever the last tween pass wrote is what the property
    /// keeps. This is also what detach does to a node-owned tween. Cancelling a terminal animation
    /// does nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Cancel() => Required.Cancel();

    /// <summary>Jumps to the end: the setter is invoked once with the final value.</summary>
    /// <remarks>
    /// The status becomes <see cref="RoutineStatus.Completed"/> and joined waiters release at their
    /// next evaluation. This is the idiom for "skip the transition, keep the result", and the
    /// primitive <see cref="CoroutineHandle.Step"/> relies on. Completing a terminal animation does
    /// nothing, so the setter is never invoked twice with the final value.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is the default handle.</exception>
    public void Complete() => Required.Complete();

    /// <inheritdoc />
    public bool Equals(AnimationHandle other) => ReferenceEquals(animation, other.animation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AnimationHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        animation is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(animation);

    /// <summary>Tests whether two handles refer to the same animation.</summary>
    public static bool operator ==(AnimationHandle left, AnimationHandle right) => left.Equals(right);

    /// <summary>Tests whether two handles refer to different animations.</summary>
    public static bool operator !=(AnimationHandle left, AnimationHandle right) => !left.Equals(right);

    internal Animation? Target => animation;

    private Animation Required =>
        animation ?? throw new InvalidOperationException(
            "The default AnimationHandle refers to no animation.");
}
