namespace Najm.Core;

/// <summary>
/// Describes the complete pixel format and quality of a render surface.
/// </summary>
/// <remarks>
/// Surface dimensions are engine- or driver-selected. Every surface is color tagged, and alpha is
/// premultiplied after crossing the rendering boundary. The zero-initialized value is invalid.
/// </remarks>
public readonly record struct SurfaceSpec
{
    /// <summary>Creates a validated surface specification.</summary>
    /// <param name="width">The positive pixel width.</param>
    /// <param name="height">The positive pixel height.</param>
    /// <param name="sampleCount">The positive requested sample count.</param>
    /// <param name="colorSpace">The mandatory color-space tag.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension or <paramref name="sampleCount"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="colorSpace"/> is not defined.</exception>
    public SurfaceSpec(
        int width,
        int height,
        int sampleCount = 1,
        ColorSpace colorSpace = ColorSpace.Srgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        if (!Enum.IsDefined(colorSpace))
        {
            throw new ArgumentException("The color-space tag is not defined.", nameof(colorSpace));
        }

        Width = width;
        Height = height;
        SampleCount = sampleCount;
        ColorSpace = colorSpace;
    }

    /// <summary>Gets the pixel width.</summary>
    public int Width { get; }

    /// <summary>Gets the pixel height.</summary>
    public int Height { get; }

    /// <summary>Gets the requested sample count.</summary>
    public int SampleCount { get; }

    /// <summary>Gets the mandatory color-space tag.</summary>
    public ColorSpace ColorSpace { get; }

    /// <summary>Gets the dimensions as a <see cref="PixelSize"/>.</summary>
    /// <exception cref="InvalidOperationException">This is a zero-initialized invalid specification.</exception>
    public PixelSize Size
    {
        get
        {
            EnsureValid();
            return new PixelSize(Width, Height);
        }
    }

    /// <summary>Gets whether this value was constructed with valid dimensions, samples, and color tag.</summary>
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        SampleCount > 0 &&
        Enum.IsDefined(ColorSpace);

    /// <summary>
    /// Returns the normalized specification used by a CPU-raster provider.
    /// </summary>
    /// <remarks>
    /// CPU Skia uses analytic antialiasing and has no multisample render-target axis, so raster
    /// providers normalize every requested sample count to one. Dimensions and color space are
    /// preserved exactly.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This is a zero-initialized invalid specification.</exception>
    public SurfaceSpec NormalizeForRaster()
    {
        EnsureValid();
        return SampleCount == 1
            ? this
            : new SurfaceSpec(Width, Height, 1, ColorSpace);
    }

    private void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("A zero-initialized or otherwise invalid SurfaceSpec cannot create a target.");
        }
    }
}
