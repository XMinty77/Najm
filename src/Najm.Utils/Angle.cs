namespace Najm.Utils;

/// <summary>
/// Represents an angle whose canonical stored representation is radians.
/// </summary>
/// <remarks>
/// Angles are not implicitly normalized: multiple turns and negative values are
/// preserved. Factories and arithmetic require finite values so invalid numeric
/// state is rejected at the API boundary.
/// </remarks>
public readonly struct Angle : IEquatable<Angle>, IComparable<Angle>
{
    private readonly double _radians;

    private Angle(double radians)
    {
        if (!double.IsFinite(radians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(radians),
                radians,
                "An angle must contain a finite number of radians.");
        }

        _radians = radians;
    }

    /// <summary>Gets the angle in radians.</summary>
    public double Radians => _radians;

    /// <summary>Gets the angle in degrees without normalizing it.</summary>
    public double Degrees => _radians * (180d / Math.PI);

    /// <summary>Gets a zero angle.</summary>
    public static Angle Zero => default;

    /// <summary>Gets one quarter-turn.</summary>
    public static Angle QuarterTurn => new(Math.PI / 2d);

    /// <summary>Gets one half-turn.</summary>
    public static Angle HalfTurn => new(Math.PI);

    /// <summary>Gets one complete turn.</summary>
    public static Angle FullTurn => new(Math.Tau);

    /// <summary>Creates an angle from a finite number of radians.</summary>
    public static Angle Rad(double radians) => new(radians);

    /// <summary>Creates an angle from a finite number of degrees.</summary>
    public static Angle Deg(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(degrees),
                degrees,
                "An angle must contain a finite number of degrees.");
        }

        return new Angle(degrees * (Math.PI / 180d));
    }

    /// <inheritdoc />
    public bool Equals(Angle other) => _radians.Equals(other._radians);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Angle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _radians.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Angle other) => _radians.CompareTo(other._radians);

    /// <summary>Adds two angles without normalizing the result.</summary>
    public static Angle operator +(Angle left, Angle right) =>
        new(left._radians + right._radians);

    /// <summary>Subtracts two angles without normalizing the result.</summary>
    public static Angle operator -(Angle left, Angle right) =>
        new(left._radians - right._radians);

    /// <summary>Negates an angle.</summary>
    public static Angle operator -(Angle value) => new(-value._radians);

    /// <summary>Scales an angle by a finite scalar.</summary>
    public static Angle operator *(Angle angle, double scalar) =>
        Scale(angle, scalar);

    /// <summary>Scales an angle by a finite scalar.</summary>
    public static Angle operator *(double scalar, Angle angle) =>
        Scale(angle, scalar);

    /// <summary>Divides an angle by a finite, non-zero scalar.</summary>
    public static Angle operator /(Angle angle, double scalar)
    {
        if (!double.IsFinite(scalar) || scalar == 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scalar),
                scalar,
                "An angle divisor must be finite and non-zero.");
        }

        return new Angle(angle._radians / scalar);
    }

    /// <summary>Returns the dimensionless ratio between two angles.</summary>
    public static double operator /(Angle left, Angle right)
    {
        if (right._radians == 0d)
        {
            throw new DivideByZeroException("Cannot divide by a zero angle.");
        }

        return left._radians / right._radians;
    }

    /// <summary>Tests two angles for exact equality.</summary>
    public static bool operator ==(Angle left, Angle right) => left.Equals(right);

    /// <summary>Tests two angles for exact inequality.</summary>
    public static bool operator !=(Angle left, Angle right) => !left.Equals(right);

    /// <summary>Tests whether the left angle is smaller than the right angle.</summary>
    public static bool operator <(Angle left, Angle right) => left.CompareTo(right) < 0;

    /// <summary>Tests whether the left angle is greater than the right angle.</summary>
    public static bool operator >(Angle left, Angle right) => left.CompareTo(right) > 0;

    /// <summary>Tests whether the left angle is no greater than the right angle.</summary>
    public static bool operator <=(Angle left, Angle right) => left.CompareTo(right) <= 0;

    /// <summary>Tests whether the left angle is no smaller than the right angle.</summary>
    public static bool operator >=(Angle left, Angle right) => left.CompareTo(right) >= 0;

    private static Angle Scale(Angle angle, double scalar)
    {
        if (!double.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scalar),
                scalar,
                "An angle scalar must be finite.");
        }

        return new Angle(angle._radians * scalar);
    }
}

