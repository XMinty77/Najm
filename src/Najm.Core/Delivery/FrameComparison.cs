namespace Najm.Core;

/// <summary>Holds two frames against each other.</summary>
/// <remarks>
/// <para>
/// The computation behind <see cref="FrameDifference"/>, kept separate from it because the result
/// is a value worth passing around and the scan that produces it is not. <see cref="AreIdentical"/>
/// exists beside <see cref="Between"/> rather than as a property of the result, because the two
/// questions have genuinely different costs: identity can stop at the first differing byte and
/// compares whole rows at a time, while a difference report has to visit every pixel to find the
/// worst case and the bounding box. Asking the cheap question through the expensive path is the
/// mistake this split is here to prevent.
/// </para>
/// <para>
/// <strong>Comparison is byte-wise and therefore format-agnostic</strong>, provided both frames
/// carry the same <see cref="PixelFormat"/>. Whether a channel is red or blue does not change
/// whether it moved, nor by how much, nor the maximum over the four — so nothing here needs to
/// know the layout. It does need the layouts to <em>match</em>, since comparing RGBA against BGRA
/// would report every coloured pixel as differing and be perfectly useless.
/// </para>
/// <para>
/// Stride padding is never compared. The bytes between a row's last pixel and the next row's start
/// are not part of the image, they are frequently uninitialized, and counting them would make two
/// identical images differ for reasons no caller could act on.
/// </para>
/// </remarks>
public static class FrameComparison
{
    /// <summary>Answers whether two frames hold exactly the same pixels.</summary>
    /// <param name="pixels">The frame under test.</param>
    /// <param name="reference">The frame it is held against.</param>
    /// <returns>
    /// True when both frames have the same size, the same format, and the same pixel bytes.
    /// </returns>
    /// <remarks>
    /// Frames of different sizes or formats return false rather than throwing: they are not the
    /// same image, which is the question that was asked. <see cref="Between"/> takes the opposite
    /// view, because a difference <em>report</em> over mismatched geometry would be a number with
    /// no meaning.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either frame is null.</exception>
    /// <exception cref="ObjectDisposedException">Either lease has been disposed.</exception>
    public static bool AreIdentical(PixelFrameLease pixels, PixelFrameLease reference)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(reference);

        if (pixels.Width != reference.Width
            || pixels.Height != reference.Height
            || pixels.Format != reference.Format)
        {
            return false;
        }

        for (var y = 0; y < pixels.Height; y++)
        {
            // Row at a time rather than byte at a time: SequenceEqual vectorizes, and the common
            // answer for a golden check is "yes", which has to read everything anyway.
            if (!pixels.Row(y).SequenceEqual(reference.Row(y)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Measures what separates two frames of the same shape.</summary>
    /// <param name="pixels">The frame under test.</param>
    /// <param name="reference">The frame it is held against.</param>
    /// <returns>A report that is <see cref="FrameDifference.AreIdentical"/> when nothing moved.</returns>
    /// <remarks>
    /// Refuses mismatched size or format loudly, unlike <see cref="AreIdentical"/> — see that
    /// member for why the two disagree deliberately.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either frame is null.</exception>
    /// <exception cref="ArgumentException">The frames differ in size or in pixel format.</exception>
    /// <exception cref="ObjectDisposedException">Either lease has been disposed.</exception>
    public static FrameDifference Between(PixelFrameLease pixels, PixelFrameLease reference)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(reference);

        if (pixels.Width != reference.Width || pixels.Height != reference.Height)
        {
            throw new ArgumentException(
                $"Cannot difference a {pixels.Width}x{pixels.Height} frame against a "
                + $"{reference.Width}x{reference.Height} one. A difference report over mismatched "
                + "geometry has no meaning; use AreIdentical, which answers false for this case.",
                nameof(reference));
        }

        if (pixels.Format != reference.Format)
        {
            throw new ArgumentException(
                $"Cannot difference a {pixels.Format} frame against a {reference.Format} one. The "
                + "comparison is byte-wise, so mismatched channel order would report every coloured "
                + "pixel as differing. Decode both to the same format first.",
                nameof(reference));
        }

        var width = pixels.Width;
        var height = pixels.Height;

        var differingPixels = 0L;
        var maxChannelDifference = 0;
        var channelDifferenceSum = 0L;
        var firstDifferenceX = -1;
        var firstDifferenceY = -1;
        var boundsLeft = int.MaxValue;
        var boundsTop = int.MaxValue;
        var boundsRight = int.MinValue;
        var boundsBottom = int.MinValue;

        for (var y = 0; y < height; y++)
        {
            var row = pixels.Row(y);
            var referenceRow = reference.Row(y);

            // The whole-row equality test is worth its own pass: an unchanged row is the common
            // case in a localized regression, and skipping it avoids four subtractions per pixel.
            if (row.SequenceEqual(referenceRow))
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var offset = x * 4;
                var worst = 0;

                for (var channel = 0; channel < 4; channel++)
                {
                    var delta = Math.Abs(row[offset + channel] - referenceRow[offset + channel]);
                    if (delta == 0)
                    {
                        continue;
                    }

                    channelDifferenceSum += delta;
                    if (delta > worst)
                    {
                        worst = delta;
                    }
                }

                if (worst == 0)
                {
                    continue;
                }

                differingPixels++;

                if (worst > maxChannelDifference)
                {
                    maxChannelDifference = worst;
                }

                if (firstDifferenceX < 0)
                {
                    // Reading order, so "the first one" means the first a viewer scanning the image
                    // would reach rather than whichever the loop happened to touch first.
                    firstDifferenceX = x;
                    firstDifferenceY = y;
                }

                if (x < boundsLeft)
                {
                    boundsLeft = x;
                }

                if (x > boundsRight)
                {
                    boundsRight = x;
                }

                if (y < boundsTop)
                {
                    boundsTop = y;
                }

                if (y > boundsBottom)
                {
                    boundsBottom = y;
                }
            }
        }

        if (differingPixels == 0L)
        {
            // An empty bounding box is reported as zeroes rather than as the sentinel extremes the
            // scan started from, so that a caller printing the box for an identical pair sees
            // nothing rather than int.MaxValue.
            return new FrameDifference(width, height, 0L, 0, 0L, -1, -1, 0, 0, -1, -1);
        }

        return new FrameDifference(
            width,
            height,
            differingPixels,
            maxChannelDifference,
            channelDifferenceSum,
            firstDifferenceX,
            firstDifferenceY,
            boundsLeft,
            boundsTop,
            boundsRight,
            boundsBottom);
    }
}
