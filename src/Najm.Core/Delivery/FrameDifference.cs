namespace Najm.Core;

/// <summary>What separates two frames of the same shape, or that nothing does.</summary>
/// <remarks>
/// <para>
/// The first question is always <see cref="AreIdentical"/>, and on the answer "no" the useful
/// follow-ups are how much of the frame moved, by how much at worst, and where. Those three
/// distinguish the cases that matter: one pixel off by one is a rounding difference, half the frame
/// off by one is a colour-space or encoder change, and a compact region off by two hundred is a
/// drawing bug with an address.
/// </para>
/// <para>
/// <strong>The magnitudes are per channel, not per pixel.</strong> A pixel differing by one in each
/// of red, green and blue reports a maximum difference of 1, not 3 and not the length of a vector.
/// This keeps every number on the 0-255 scale of the pixels themselves, which is the scale the
/// answer gets compared against ("worst case one level" is a verdict; "worst case 1.73" is not).
/// Alpha counts as a channel like any other.
/// </para>
/// <para>
/// <c>default(FrameDifference)</c> is not a meaningful value; obtain one from
/// <see cref="FrameComparison"/>.
/// </para>
/// </remarks>
public readonly struct FrameDifference
{
    internal FrameDifference(
        int width,
        int height,
        long differingPixels,
        int maxChannelDifference,
        long channelDifferenceSum,
        int firstDifferenceX,
        int firstDifferenceY,
        int boundsLeft,
        int boundsTop,
        int boundsRight,
        int boundsBottom)
    {
        Width = width;
        Height = height;
        DifferingPixels = differingPixels;
        MaxChannelDifference = maxChannelDifference;
        ChannelDifferenceSum = channelDifferenceSum;
        FirstDifferenceX = firstDifferenceX;
        FirstDifferenceY = firstDifferenceY;
        BoundsLeft = boundsLeft;
        BoundsTop = boundsTop;
        BoundsRight = boundsRight;
        BoundsBottom = boundsBottom;
    }

    /// <summary>Gets whether the two frames are byte-identical.</summary>
    /// <remarks>
    /// This is the check the project reaches for most: goldens, no-op parameter verification, and
    /// "did removing that workaround change anything" are all this question. It is exact — no
    /// tolerance, no perceptual weighting — because the whole value of the answer is that it admits
    /// no argument.
    /// </remarks>
    public bool AreIdentical => DifferingPixels == 0L;

    /// <summary>Gets the compared frames' shared width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the compared frames' shared height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of pixels compared.</summary>
    public long PixelCount => (long)Width * Height;

    /// <summary>Gets how many pixels differ in at least one channel.</summary>
    public long DifferingPixels { get; }

    /// <summary>Gets the share of pixels that differ, in zero to one.</summary>
    public double DifferingFraction => PixelCount == 0L ? 0d : (double)DifferingPixels / PixelCount;

    /// <summary>Gets the largest absolute difference in any single channel of any pixel.</summary>
    public int MaxChannelDifference { get; }

    /// <summary>Gets the total absolute channel difference summed over every channel of every pixel.</summary>
    /// <remarks>
    /// Exposed because it is the one quantity from which any other mean can be rebuilt — over
    /// differing pixels only, over three channels instead of four, per megapixel — without
    /// recomparing the frames.
    /// </remarks>
    public long ChannelDifferenceSum { get; }

    /// <summary>Gets the mean absolute channel difference over every channel of every pixel.</summary>
    /// <remarks>
    /// The denominator is four channels times every pixel, matching pixels included, so this is the
    /// average level a channel moved across the whole frame. It goes to nearly zero for a frame that
    /// differs sharply in one small place, which is why it is read next to
    /// <see cref="MaxChannelDifference"/> and <see cref="DifferingFraction"/> rather than instead of
    /// them.
    /// </remarks>
    public double MeanChannelDifference =>
        PixelCount == 0L ? 0d : ChannelDifferenceSum / (double)(PixelCount * 4L);

    /// <summary>Gets the column of the first differing pixel in raster order, or -1 if identical.</summary>
    public int FirstDifferenceX { get; }

    /// <summary>Gets the row of the first differing pixel in raster order, or -1 if identical.</summary>
    public int FirstDifferenceY { get; }

    /// <summary>Gets the leftmost column containing a difference, or -1 if identical.</summary>
    public int BoundsLeft { get; }

    /// <summary>Gets the topmost row containing a difference, or -1 if identical.</summary>
    public int BoundsTop { get; }

    /// <summary>Gets the rightmost column containing a difference, inclusive, or -1 if identical.</summary>
    public int BoundsRight { get; }

    /// <summary>Gets the bottommost row containing a difference, inclusive, or -1 if identical.</summary>
    public int BoundsBottom { get; }

    /// <summary>Gets the width of the region containing every difference, or zero if identical.</summary>
    public int BoundsWidth => AreIdentical ? 0 : BoundsRight - BoundsLeft + 1;

    /// <summary>Gets the height of the region containing every difference, or zero if identical.</summary>
    public int BoundsHeight => AreIdentical ? 0 : BoundsBottom - BoundsTop + 1;

    /// <summary>Renders the comparison as one line, for a report or an assertion message.</summary>
    public override string ToString() =>
        AreIdentical
            ? $"identical ({Width}x{Height})"
            : $"{DifferingPixels} of {PixelCount} pixels differ ({DifferingFraction:P3}), " +
                $"worst {MaxChannelDifference} levels, mean {MeanChannelDifference:0.###} levels, " +
                $"first at ({FirstDifferenceX}, {FirstDifferenceY}), " +
                $"bounds {BoundsWidth}x{BoundsHeight} at ({BoundsLeft}, {BoundsTop})";
}
