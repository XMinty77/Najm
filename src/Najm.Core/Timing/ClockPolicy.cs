namespace Najm.Core;

/// <summary>Describes how a host advances one scene clock.</summary>
/// <remarks>
/// A policy contains configuration only and never reads a wall clock. The
/// zero-initialized value is invalid; inspect <see cref="IsValid"/> before using
/// a policy received from untrusted or optional state.
/// </remarks>
public readonly struct ClockPolicy : IEquatable<ClockPolicy>
{
    private readonly ClockPolicyKind kind;
    private readonly double value;

    private ClockPolicy(ClockPolicyKind kind, double value)
    {
        this.kind = kind;
        this.value = value;
    }

    /// <summary>Gets whether this value was created by <see cref="Fixed"/> or <see cref="Live"/>.</summary>
    public bool IsValid =>
        kind is ClockPolicyKind.Fixed or ClockPolicyKind.Live &&
        double.IsFinite(value) &&
        value > 0d;

    /// <summary>
    /// Gets whether this is a fixed-step policy.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public bool IsFixedStep
    {
        get
        {
            EnsureValid();
            return kind == ClockPolicyKind.Fixed;
        }
    }

    /// <summary>Gets fixed-step frames per simulated second.</summary>
    /// <exception cref="InvalidOperationException">This is not a valid fixed-step policy.</exception>
    public double FramesPerSecond
    {
        get
        {
            EnsureKind(ClockPolicyKind.Fixed, "Frames per second exist only on a fixed-step policy.");
            return value;
        }
    }

    /// <summary>Gets the maximum live delta, in seconds.</summary>
    /// <exception cref="InvalidOperationException">This is not a valid live policy.</exception>
    public double MaxDt
    {
        get
        {
            EnsureKind(ClockPolicyKind.Live, "A maximum live delta exists only on a live policy.");
            return value;
        }
    }

    /// <summary>Creates a fixed-step policy with a finite, positive frame rate.</summary>
    /// <remarks>
    /// The reciprocal frame duration must also be finite and positive. This
    /// rejects impractically tiny rates that cannot produce a finite tick delta.
    /// </remarks>
    public static ClockPolicy Fixed(double framesPerSecond)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesPerSecond),
                framesPerSecond,
                "Fixed-step frames per second must be finite and positive.");
        }

        var dt = 1d / framesPerSecond;
        if (!double.IsFinite(dt) || dt <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesPerSecond),
                framesPerSecond,
                "Fixed-step frames per second must produce a finite, positive delta.");
        }

        return new ClockPolicy(ClockPolicyKind.Fixed, framesPerSecond);
    }

    /// <summary>Creates a live policy with a finite, positive maximum delta.</summary>
    public static ClockPolicy Live(double maxDt)
    {
        if (!double.IsFinite(maxDt) || maxDt <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDt),
                maxDt,
                "The maximum live delta must be finite and positive.");
        }

        return new ClockPolicy(ClockPolicyKind.Live, maxDt);
    }

    /// <inheritdoc />
    public bool Equals(ClockPolicy other) => kind == other.kind && value.Equals(other.value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ClockPolicy other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((byte)kind, value);

    /// <summary>Tests two policies for exact equality.</summary>
    public static bool operator ==(ClockPolicy left, ClockPolicy right) => left.Equals(right);

    /// <summary>Tests two policies for exact inequality.</summary>
    public static bool operator !=(ClockPolicy left, ClockPolicy right) => !left.Equals(right);

    private void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "The zero-initialized ClockPolicy is invalid. Use ClockPolicy.Fixed or ClockPolicy.Live.");
        }
    }

    private void EnsureKind(ClockPolicyKind expected, string message)
    {
        EnsureValid();
        if (kind != expected)
        {
            throw new InvalidOperationException(message);
        }
    }

    private enum ClockPolicyKind : byte
    {
        Invalid,
        Fixed,
        Live,
    }
}

