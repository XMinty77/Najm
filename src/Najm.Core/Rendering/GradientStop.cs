using Najm.Utils;

namespace Najm.Core;

/// <summary>Pairs a normalized position along a gradient with the color reached there.</summary>
/// <remarks>
/// <c>default(GradientStop)</c> is transparent black at offset zero, which is a valid stop.
/// Stops are compared by exact value so that two independently built gradients that describe the
/// same ramp are one cache key in a backend's descriptor cache.
/// </remarks>
public readonly struct GradientStop : IEquatable<GradientStop>
{
    /// <summary>Creates a gradient stop.</summary>
    /// <param name="offset">A finite position in the closed interval [0, 1].</param>
    /// <param name="color">The sRGB-referenced color reached at that position.</param>
    public GradientStop(float offset, Color color)
    {
        if (!float.IsFinite(offset) || offset is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                "Gradient stop offsets must be finite and in the closed interval [0, 1].");
        }

        Offset = offset;
        Color = color;
    }

    /// <summary>Gets the position in the closed interval [0, 1].</summary>
    public float Offset { get; }

    /// <summary>Gets the sRGB-referenced color reached at <see cref="Offset"/>.</summary>
    public Color Color { get; }

    /// <inheritdoc />
    public bool Equals(GradientStop other) => Offset.Equals(other.Offset) && Color.Equals(other.Color);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GradientStop other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Offset, Color);

    /// <summary>Tests two gradient stops for exact value equality.</summary>
    public static bool operator ==(GradientStop left, GradientStop right) => left.Equals(right);

    /// <summary>Tests two gradient stops for exact value inequality.</summary>
    public static bool operator !=(GradientStop left, GradientStop right) => !left.Equals(right);
}
