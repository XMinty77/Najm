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
/// <strong>"Different shapes" is one of the answers.</strong> Two frames of different sizes are a
/// real pair to hold against each other — a wrong <c>--size</c> flag produces one — and the honest
/// report for them is that their geometry differs, not an exception. Such a report has
/// <see cref="HasMatchingGeometry"/> false, <see cref="AreIdentical"/> false, and <strong>every
/// magnitude zero, because nothing was measured</strong>: no pixel in one frame has a counterpart
/// in the other. Read the verdict, not the numbers — a caller that branches on
/// <c>DifferingPixels == 0</c> alone will read a geometry mismatch as a match.
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
        int referenceWidth,
        int referenceHeight,
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
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
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

    /// <summary>Builds the report for two frames whose shapes do not correspond.</summary>
    internal static FrameDifference Mismatched(
        int width,
        int height,
        int referenceWidth,
        int referenceHeight) =>
        new(width, height, referenceWidth, referenceHeight, 0L, 0, 0L, -1, -1, 0, 0, 0, 0);

    /// <summary>Gets whether the two frames are byte-identical.</summary>
    /// <remarks>
    /// This is the check the project reaches for most: goldens, no-op parameter verification, and
    /// "did removing that workaround change anything" are all this question. It is exact — no
    /// tolerance, no perceptual weighting — because the whole value of the answer is that it admits
    /// no argument. Frames of different shapes are never identical, whatever their pixels hold.
    /// </remarks>
    public bool AreIdentical => HasMatchingGeometry && DifferingPixels == 0L;

    /// <summary>Gets whether the two frames have the same width and height.</summary>
    /// <remarks>
    /// False makes every other number here meaningless rather than merely zero: nothing was
    /// compared, because there was no correspondence to compare along. Test this before reading a
    /// magnitude, or read <see cref="AreIdentical"/>, which already accounts for it.
    /// </remarks>
    public bool HasMatchingGeometry => Width == ReferenceWidth && Height == ReferenceHeight;

    /// <summary>Gets the width in pixels of the frame under test.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels of the frame under test.</summary>
    public int Height { get; }

    /// <summary>Gets the width in pixels of the reference frame, which equals <see cref="Width"/>
    /// unless the geometry differs.</summary>
    public int ReferenceWidth { get; }

    /// <summary>Gets the height in pixels of the reference frame, which equals <see cref="Height"/>
    /// unless the geometry differs.</summary>
    public int ReferenceHeight { get; }

    /// <summary>Gets the number of pixels compared, which is zero when the geometry differs.</summary>
    public long PixelCount => HasMatchingGeometry ? (long)Width * Height : 0L;

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

    /// <summary>Gets the leftmost column containing a difference, or zero if identical.</summary>
    /// <remarks>
    /// All four bounds read zero for an identical pair, so the empty box prints as nothing rather
    /// than as the sentinel extremes a scan starts from. Test emptiness with
    /// <see cref="AreIdentical"/> or <see cref="BoundsWidth"/>, never by looking for a sentinel
    /// here — unlike <see cref="FirstDifferenceX"/>, which is -1 because zero is a real position.
    /// </remarks>
    public int BoundsLeft { get; }

    /// <summary>Gets the topmost row containing a difference, or zero if identical.</summary>
    public int BoundsTop { get; }

    /// <summary>Gets the rightmost column containing a difference, inclusive, or zero if identical.</summary>
    public int BoundsRight { get; }

    /// <summary>Gets the bottommost row containing a difference, inclusive, or zero if identical.</summary>
    public int BoundsBottom { get; }

    /// <summary>
    /// Gets the width of the region containing every difference, or zero if identical or if the
    /// geometry differs.
    /// </summary>
    public int BoundsWidth => HasNoLocatedDifference ? 0 : BoundsRight - BoundsLeft + 1;

    /// <summary>
    /// Gets the height of the region containing every difference, or zero if identical or if the
    /// geometry differs.
    /// </summary>
    public int BoundsHeight => HasNoLocatedDifference ? 0 : BoundsBottom - BoundsTop + 1;

    /// <summary>
    /// Gets whether there is no located difference to describe — either nothing moved, or nothing
    /// was compared.
    /// </summary>
    /// <remarks>
    /// The empty box is four zeroes in both cases, so the extent has to be derived from the verdict
    /// rather than from the bounds; otherwise an empty box would measure one pixel across.
    /// </remarks>
    private bool HasNoLocatedDifference => !HasMatchingGeometry || DifferingPixels == 0L;

    /// <summary>Renders the comparison as one line, for a report or an assertion message.</summary>
    public override string ToString() =>
        !HasMatchingGeometry
            ? $"different geometry: {Width}x{Height} against {ReferenceWidth}x{ReferenceHeight}"
            : AreIdentical
            ? $"identical ({Width}x{Height})"
            : $"{DifferingPixels} of {PixelCount} pixels differ ({DifferingFraction:P3}), " +
                $"worst {MaxChannelDifference} levels, mean {MeanChannelDifference:0.###} levels, " +
                $"first at ({FirstDifferenceX}, {FirstDifferenceY}), " +
                $"bounds {BoundsWidth}x{BoundsHeight} at ({BoundsLeft}, {BoundsTop})";
}
