using Najm.Utils;

namespace Najm.Core;

/// <summary>Pairs a normalized position along a gradient with the color reached there.</summary>
/// <remarks>
/// <para>
/// <c>default(GradientStop)</c> is transparent black at offset zero, which is a valid stop.
/// Stops are compared by exact value so that two independently built gradients that describe the
/// same ramp are one cache key in a backend's descriptor cache.
/// </para>
/// <para>
/// <strong>Between two stops, all four channels interpolate independently on straight
/// (unpremultiplied) values.</strong> RGB does not know about alpha. That matters at exactly one
/// place — the transparent end of a fade — and it matters every time:
/// </para>
/// <para>
/// A ramp from <c>Color.Srgb(1, 0.8f, 0.3f)</c> to <see cref="Najm.Utils.Color.Transparent"/> is a
/// ramp to transparent <em>black</em>, so halfway along it the color is half-alpha
/// <c>(0.5, 0.4, 0.15)</c> — a muddy brown — instead of half-alpha amber. Composited, that reads as
/// a grey bruise ringing the fade, and it looks like a rendering bug rather than the API misuse it
/// is. Writing the far stop as <c>color.Fade()</c> (see <see cref="Najm.Utils.Color.Fade"/>) keeps
/// RGB constant and moves only coverage, which is what "fades out" means. The two spellings agree
/// only when the color is black, which is why the mistake hides on dark backgrounds.
/// </para>
/// <para>
/// This is the interpolation model of SVG, CSS, Skia, and PDF; the engine does not switch it per
/// brush. <see cref="Brush.LinearFade"/> and <see cref="Brush.RadialFade"/> build the correct
/// two-stop ramp for the common case.
/// </para>
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
