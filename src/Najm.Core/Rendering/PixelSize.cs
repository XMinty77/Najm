namespace Najm.Core;

/// <summary>
/// A positive two-dimensional size measured in device pixels.
/// </summary>
public readonly record struct PixelSize
{
    /// <summary>Creates a pixel size.</summary>
    /// <param name="width">The positive width in pixels.</param>
    /// <param name="height">The positive height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets whether this value is the invalid zero-initialized value rather than a constructed size.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
