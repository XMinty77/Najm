using System.Runtime.CompilerServices;

namespace Najm.Utils;

/// <summary>Maps normalized tween progress to eased progress.</summary>
/// <remarks>
/// Implementations must not retain per-evaluation state. Najm invokes timing
/// functions on steady-state frame paths, so evaluation should not allocate.
/// </remarks>
public interface ITimingFunction
{
    /// <summary>
    /// Evaluates finite progress. Implementations may extrapolate and must not
    /// assume the input was clamped unless their own contract says otherwise.
    /// </summary>
    float Evaluate(float progress);
}

/// <summary>
/// An allocation-free built-in timing function selected from <see cref="Ease"/>.
/// </summary>
/// <remarks>
/// This is a value type. Store it directly on hot paths to avoid interface
/// boxing; converting it to <see cref="ITimingFunction"/> may box once at setup,
/// but evaluation itself never allocates.
/// </remarks>
public readonly struct TimingFunction : ITimingFunction, IEquatable<TimingFunction>
{
    private readonly BuiltInEase _kind;

    internal TimingFunction(BuiltInEase kind) => _kind = kind;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Evaluate(float progress)
    {
        if (!float.IsFinite(progress))
        {
            throw new ArgumentOutOfRangeException(
                nameof(progress),
                progress,
                "Timing progress must be finite.");
        }

        return _kind switch
        {
            BuiltInEase.Linear => progress,
            BuiltInEase.InQuad => progress * progress,
            BuiltInEase.OutQuad => 1f - ((1f - progress) * (1f - progress)),
            BuiltInEase.InOutQuad => progress < 0.5f
                ? 2f * progress * progress
                : 1f - (Square((-2f * progress) + 2f) / 2f),
            BuiltInEase.InCubic => progress * progress * progress,
            BuiltInEase.OutCubic => 1f - Cube(1f - progress),
            BuiltInEase.InOutCubic => progress < 0.5f
                ? 4f * progress * progress * progress
                : 1f - (Cube((-2f * progress) + 2f) / 2f),
            _ => throw new InvalidOperationException("Unknown built-in timing function."),
        };
    }

    /// <inheritdoc />
    public bool Equals(TimingFunction other) => _kind == other._kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TimingFunction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)_kind;

    /// <summary>Tests two built-in timing functions for equality.</summary>
    public static bool operator ==(TimingFunction left, TimingFunction right) => left.Equals(right);

    /// <summary>Tests two built-in timing functions for inequality.</summary>
    public static bool operator !=(TimingFunction left, TimingFunction right) => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Square(float value) => value * value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Cube(float value) => value * value * value;
}

/// <summary>Allocation-free built-in easing functions, named direction first.</summary>
public static class Ease
{
    /// <summary>Gets the identity timing function.</summary>
    public static TimingFunction Linear { get; } = new(BuiltInEase.Linear);

    /// <summary>Gets quadratic acceleration.</summary>
    public static TimingFunction InQuad { get; } = new(BuiltInEase.InQuad);

    /// <summary>Gets quadratic deceleration.</summary>
    public static TimingFunction OutQuad { get; } = new(BuiltInEase.OutQuad);

    /// <summary>Gets symmetric quadratic acceleration then deceleration.</summary>
    public static TimingFunction InOutQuad { get; } = new(BuiltInEase.InOutQuad);

    /// <summary>Gets cubic acceleration.</summary>
    public static TimingFunction InCubic { get; } = new(BuiltInEase.InCubic);

    /// <summary>Gets cubic deceleration.</summary>
    public static TimingFunction OutCubic { get; } = new(BuiltInEase.OutCubic);

    /// <summary>Gets symmetric cubic acceleration then deceleration.</summary>
    public static TimingFunction InOutCubic { get; } = new(BuiltInEase.InOutCubic);
}

internal enum BuiltInEase : byte
{
    Linear,
    InQuad,
    OutQuad,
    InOutQuad,
    InCubic,
    OutCubic,
    InOutCubic,
}

