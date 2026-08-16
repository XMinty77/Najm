namespace Najm.Core;

/// <summary>A backend-neutral value describing a stroke's on/off dash pattern.</summary>
/// <remarks>
/// <para>
/// Intervals alternate painted and unpainted lengths starting with a painted one, are expressed in
/// local units like every other size, and must come in pairs. The phase is how far into the pattern
/// each contour starts.
/// </para>
/// <para>
/// Storage and equality follow <see cref="Brush"/>: the constructor copies the caller's span into
/// one array the value never hands out, and equality compares interval <em>contents</em> so a
/// backend can key its dash-effect cache by value (NAJM-SKIA II.2) instead of by array reference.
/// <c>default(StrokeDash)</c> carries no intervals and is not a usable pattern; a paint rejects it.
/// </para>
/// </remarks>
public readonly struct StrokeDash : IEquatable<StrokeDash>
{
    private readonly float[]? intervals;

    /// <summary>Creates a dash pattern.</summary>
    /// <param name="intervals">
    /// An even count of at least two finite, positive on/off lengths in local units; copied.
    /// </param>
    /// <param name="phase">The finite, nonnegative local-unit offset into the pattern.</param>
    public StrokeDash(ReadOnlySpan<float> intervals, float phase = 0f)
    {
        if (intervals.Length < 2 || intervals.Length % 2 != 0)
        {
            throw new ArgumentException(
                "A dash pattern requires an even count of at least two intervals.",
                nameof(intervals));
        }
        foreach (var interval in intervals)
        {
            if (!float.IsFinite(interval) || interval <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(intervals),
                    interval,
                    "Dash intervals must be finite and positive.");
            }
        }
        if (!float.IsFinite(phase) || phase < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                "The dash phase must be finite and nonnegative.");
        }

        this.intervals = intervals.ToArray();
        Phase = phase;
    }

    /// <summary>
    /// Gets the alternating painted and unpainted lengths in local units. The span is empty only
    /// for <c>default(StrokeDash)</c>, and it views a payload no caller can mutate.
    /// </summary>
    public ReadOnlySpan<float> Intervals => intervals;

    /// <summary>Gets the nonnegative local-unit offset into the pattern.</summary>
    public float Phase { get; }

    /// <summary>Gets whether this value carries no intervals and therefore cannot dash a stroke.</summary>
    public bool IsEmpty => intervals is null;

    /// <inheritdoc />
    public bool Equals(StrokeDash other) =>
        Phase.Equals(other.Phase) && Intervals.SequenceEqual(other.Intervals);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StrokeDash other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Phase);
        foreach (var interval in Intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    /// <summary>Tests two dash patterns for value equality, comparing interval contents.</summary>
    public static bool operator ==(StrokeDash left, StrokeDash right) => left.Equals(right);

    /// <summary>Tests two dash patterns for value inequality, comparing interval contents.</summary>
    public static bool operator !=(StrokeDash left, StrokeDash right) => !left.Equals(right);
}
