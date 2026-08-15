namespace Najm.Core;

/// <summary>Contains immutable post-advance simulation time for one tick.</summary>
/// <remarks>
/// Elapsed time and delta are finite, non-negative seconds. Frame indices start
/// at zero. The zero-initialized value is deliberately invalid and all semantic
/// accessors throw for it; use <see cref="IsValid"/> when a default value may be
/// present.
/// </remarks>
public readonly struct TimeInfo : IEquatable<TimeInfo>
{
    private readonly double elapsed;
    private readonly double dt;
    private readonly long frame;
    private readonly bool isFixedStep;
    private readonly bool isValid;

    /// <summary>Creates validated time data for one post-advance tick.</summary>
    /// <param name="elapsed">Finite, non-negative elapsed simulation seconds after this tick.</param>
    /// <param name="dt">Finite, non-negative simulation seconds consumed by this tick.</param>
    /// <param name="frame">The non-negative zero-based frame index.</param>
    /// <param name="isFixedStep">Whether a fixed-step clock produced the tick.</param>
    public TimeInfo(double elapsed, double dt, long frame, bool isFixedStep)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "Elapsed time must be finite and non-negative.");
        }
        if (!double.IsFinite(dt) || dt < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "Tick delta must be finite and non-negative.");
        }
        if (dt > elapsed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dt),
                dt,
                "Tick delta cannot exceed post-advance elapsed time.");
        }
        if (frame < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                frame,
                "Frame indices must be non-negative.");
        }

        this.elapsed = elapsed;
        this.dt = dt;
        this.frame = frame;
        this.isFixedStep = isFixedStep;
        isValid = true;
    }

    /// <summary>Gets whether this is constructed rather than zero-initialized time data.</summary>
    public bool IsValid => isValid;

    /// <summary>Gets post-advance elapsed simulation seconds.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public double Elapsed
    {
        get
        {
            EnsureValid();
            return elapsed;
        }
    }

    /// <summary>Gets simulation seconds consumed by this tick.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public double Dt
    {
        get
        {
            EnsureValid();
            return dt;
        }
    }

    /// <summary>Gets the zero-based frame index.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public long Frame
    {
        get
        {
            EnsureValid();
            return frame;
        }
    }

    /// <summary>Gets whether a fixed-step clock produced this tick.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default value.</exception>
    public bool IsFixedStep
    {
        get
        {
            EnsureValid();
            return isFixedStep;
        }
    }

    /// <inheritdoc />
    public bool Equals(TimeInfo other) =>
        isValid == other.isValid &&
        elapsed.Equals(other.elapsed) &&
        dt.Equals(other.dt) &&
        frame == other.frame &&
        isFixedStep == other.isFixedStep;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TimeInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(elapsed, dt, frame, isFixedStep, isValid);

    /// <summary>Tests two time values for exact equality.</summary>
    public static bool operator ==(TimeInfo left, TimeInfo right) => left.Equals(right);

    /// <summary>Tests two time values for exact inequality.</summary>
    public static bool operator !=(TimeInfo left, TimeInfo right) => !left.Equals(right);

    private void EnsureValid()
    {
        if (!isValid)
        {
            throw new InvalidOperationException(
                "The zero-initialized TimeInfo is invalid and does not describe a tick.");
        }
    }
}

