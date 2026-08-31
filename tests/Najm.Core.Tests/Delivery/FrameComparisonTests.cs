namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The single most-used check in this project's history — "is this byte-identical to the
/// reference?" — and the report that answers what moved when it is not.
/// </summary>
/// <remarks>
/// The two entry points disagree about mismatched geometry on purpose, so both halves of that
/// disagreement are pinned here: a differently sized frame is <em>not the same image</em>, which
/// <see cref="FrameComparison.AreIdentical"/> answers with false, while a difference report over it
/// would be a number with no meaning, which <see cref="FrameComparison.Between"/> refuses.
/// </remarks>
[TestClass]
public sealed class FrameComparisonTests
{
    [TestMethod]
    public void IdenticalFramesAreIdenticalAndTheirDifferenceIsEmpty()
    {
        using var first = TestFrame.Uniform(5, 4, 12, 34, 56, 200);
        using var second = TestFrame.Uniform(5, 4, 12, 34, 56, 200);

        Assert.IsTrue(FrameComparison.AreIdentical(first, second));

        var difference = FrameComparison.Between(first, second);

        Assert.IsTrue(difference.AreIdentical);
        Assert.AreEqual(0L, difference.DifferingPixels);
        Assert.AreEqual(0d, difference.DifferingFraction);
        Assert.AreEqual(0, difference.MaxChannelDifference);
        Assert.AreEqual(0L, difference.ChannelDifferenceSum);
        Assert.AreEqual(0d, difference.MeanChannelDifference);
        Assert.AreEqual(20L, difference.PixelCount);

        Assert.AreEqual(-1, difference.FirstDifferenceX, "No position, and 0 is a real position.");
        Assert.AreEqual(-1, difference.FirstDifferenceY);

        // An empty box reads as zeroes rather than as the sentinel extremes the scan started from,
        // so a caller printing it for an identical pair sees nothing rather than int.MaxValue.
        Assert.AreEqual(0, difference.BoundsLeft);
        Assert.AreEqual(0, difference.BoundsTop);
        Assert.AreEqual(0, difference.BoundsRight);
        Assert.AreEqual(0, difference.BoundsBottom);
        Assert.AreEqual(0, difference.BoundsWidth);
        Assert.AreEqual(0, difference.BoundsHeight);
        Assert.Contains("identical", difference.ToString());
    }

    /// <summary>One channel of one pixel, off by one — the smallest difference there is.</summary>
    /// <remarks>
    /// This is the difference a dithered shader or a changed rounding mode produces, so it has to be
    /// visible rather than lost in a tolerance. Every magnitude below is derivable by hand: one
    /// channel moved one level, over eight pixels of four channels each.
    /// </remarks>
    [TestMethod]
    public void ASingleChannelOffByOneIsFoundAndLocated()
    {
        using var reference = TestFrame.Uniform(4, 2, 100, 100, 100);
        using var pixels = TestFrame.Uniform(4, 2, 100, 100, 100);
        TestFrame.Set(pixels, 2, 1, 100, 101, 100);

        Assert.IsFalse(FrameComparison.AreIdentical(pixels, reference));

        var difference = FrameComparison.Between(pixels, reference);

        Assert.IsFalse(difference.AreIdentical);
        Assert.AreEqual(1L, difference.DifferingPixels);
        Assert.AreEqual(1d / 8d, difference.DifferingFraction, 1e-12d);
        Assert.AreEqual(1, difference.MaxChannelDifference);
        Assert.AreEqual(1L, difference.ChannelDifferenceSum);
        Assert.AreEqual(1d / 32d, difference.MeanChannelDifference, 1e-12d, "Eight pixels of four channels.");
        Assert.AreEqual(2, difference.FirstDifferenceX);
        Assert.AreEqual(1, difference.FirstDifferenceY);
        Assert.AreEqual(1, difference.BoundsWidth, "One pixel is a one-by-one box, not a zero-sized one.");
        Assert.AreEqual(1, difference.BoundsHeight);
        Assert.AreEqual(2, difference.BoundsLeft);
        Assert.AreEqual(2, difference.BoundsRight);
        Assert.AreEqual(1, difference.BoundsTop);
        Assert.AreEqual(1, difference.BoundsBottom);
    }

    /// <summary>
    /// The bounding box spans every difference, and the first one is the first in reading order.
    /// </summary>
    /// <remarks>
    /// The three differing pixels are placed so that no single one of them supplies the whole box
    /// and so that the first in reading order is not the leftmost: (5, 1) comes first because row 1
    /// precedes row 3, even though (2, 3) is further left. An implementation reporting "the leftmost
    /// difference" or "the last one seen" answers differently on all three counts.
    /// </remarks>
    [TestMethod]
    public void TheBoundingBoxSpansEveryDifferenceAndTheFirstIsInReadingOrder()
    {
        using var reference = TestFrame.Uniform(8, 5, 20, 20, 20);
        using var pixels = TestFrame.Uniform(8, 5, 20, 20, 20);
        TestFrame.Set(pixels, 5, 1, 21, 20, 20);
        TestFrame.Set(pixels, 2, 3, 20, 23, 20);
        TestFrame.Set(pixels, 6, 3, 20, 20, 22);

        var difference = FrameComparison.Between(pixels, reference);

        Assert.AreEqual(3L, difference.DifferingPixels);
        Assert.AreEqual(5, difference.FirstDifferenceX, "Reading order: row 1 before row 3.");
        Assert.AreEqual(1, difference.FirstDifferenceY);
        Assert.AreEqual(2, difference.BoundsLeft);
        Assert.AreEqual(1, difference.BoundsTop);
        Assert.AreEqual(6, difference.BoundsRight, "Inclusive, so the rightmost differing column itself.");
        Assert.AreEqual(3, difference.BoundsBottom);
        Assert.AreEqual(5, difference.BoundsWidth, "Columns 2 through 6 inclusive.");
        Assert.AreEqual(3, difference.BoundsHeight, "Rows 1 through 3 inclusive.");
        Assert.AreEqual(3, difference.MaxChannelDifference, "The worst single channel, which is green.");
        Assert.AreEqual(6L, difference.ChannelDifferenceSum, "1 + 3 + 2.");
    }

    /// <summary>
    /// The maximum is over channels as well as pixels, and it is not a per-pixel magnitude.
    /// </summary>
    [TestMethod]
    public void TheWorstCaseIsTheLargestSingleChannelMoveAnywhereInTheFrame()
    {
        using var reference = TestFrame.Uniform(3, 1, 10, 10, 10, 255);
        using var pixels = TestFrame.Uniform(3, 1, 10, 10, 10, 255);
        TestFrame.Set(pixels, 0, 0, 13, 13, 13, 255);
        TestFrame.Set(pixels, 2, 0, 10, 10, 10, 55);

        var difference = FrameComparison.Between(pixels, reference);

        Assert.AreEqual(2L, difference.DifferingPixels);
        Assert.AreEqual(
            200,
            difference.MaxChannelDifference,
            "Alpha moved 200 levels; it is a channel like any other.");
        Assert.AreEqual(209L, difference.ChannelDifferenceSum, "Three moves of 3, plus one of 200.");
        Assert.AreEqual(209d / 12d, difference.MeanChannelDifference, 1e-12d);
    }

    /// <summary>
    /// Mismatched geometry: false from one entry point and a report saying so from the other, which
    /// is what makes the two composable.
    /// </summary>
    /// <remarks>
    /// The pair used to disagree — one answered, the other threw — so the natural sequence of the
    /// two, "is it identical, and if not what moved", was an unhandled exception for the pair a
    /// mistyped output size produces. Every magnitude in the report is zero because nothing was
    /// compared, which is why <see cref="FrameDifference.HasMatchingGeometry"/> and not a magnitude
    /// is the thing to branch on.
    /// </remarks>
    [TestMethod]
    public void DifferentlySizedFramesAreNotIdenticalAndAreReportedAsSuch()
    {
        using var small = TestFrame.Uniform(4, 4, 0, 0, 0);
        using var wide = TestFrame.Uniform(5, 4, 0, 0, 0);
        using var tall = TestFrame.Uniform(4, 5, 0, 0, 0);

        Assert.IsFalse(FrameComparison.AreIdentical(small, wide));
        Assert.IsFalse(FrameComparison.AreIdentical(small, tall));

        var difference = FrameComparison.Between(small, wide);

        Assert.IsFalse(difference.HasMatchingGeometry);
        Assert.IsFalse(difference.AreIdentical, "different shapes are never identical");
        Assert.AreEqual(4, difference.Width, "the frame under test's shape");
        Assert.AreEqual(4, difference.Height);
        Assert.AreEqual(5, difference.ReferenceWidth, "and the reference's, both named");
        Assert.AreEqual(4, difference.ReferenceHeight);
        Assert.AreEqual(0L, difference.PixelCount, "nothing was compared, so nothing was counted");
        Assert.AreEqual(0L, difference.DifferingPixels);
        Assert.AreEqual(0, difference.MaxChannelDifference);
        Assert.AreEqual(-1, difference.FirstDifferenceX);
        Assert.AreEqual(0, difference.BoundsWidth, "an empty box is empty, not one pixel across");
        Assert.AreEqual(0, difference.BoundsHeight);
        Assert.Contains("4x4", difference.ToString(), "the summary has to name both shapes");
        Assert.Contains("5x4", difference.ToString());

        Assert.IsFalse(FrameComparison.Between(small, tall).HasMatchingGeometry);
        Assert.IsTrue(
            FrameComparison.Between(small, TestFrame.Uniform(4, 4, 1, 1, 1)).HasMatchingGeometry,
            "and a matching pair reports matching geometry, identical or not");
    }

    /// <summary>
    /// Mismatched pixel formats, likewise — and this is the case where a byte-wise comparison would
    /// otherwise report a confident, entirely wrong answer.
    /// </summary>
    [TestMethod]
    public void DifferentlyFormattedFramesAreNotIdenticalAndCannotBeDifferenced()
    {
        using var rgba = TestFrame.Uniform(2, 2, 200, 100, 50, 255);
        using var bgra = TestFrame.Uniform(2, 2, 200, 100, 50, 255, PixelFormat.Bgra8888Premul);

        Assert.IsFalse(
            FrameComparison.AreIdentical(rgba, bgra),
            "The same colours in a different byte order are not the same frame to a byte-wise check.");

        var failure = Assert.ThrowsExactly<ArgumentException>(() => FrameComparison.Between(rgba, bgra));
        Assert.Contains("Rgba8888", failure.Message);
        Assert.Contains("Bgra8888Premul", failure.Message);
    }

    /// <summary>
    /// Stride padding is not image data, so two frames that agree on their pixels are identical
    /// however their padding differs.
    /// </summary>
    /// <remarks>
    /// Padding is routinely uninitialized. A comparison that read it would make a golden check fail
    /// for a reason no caller could act on, and — worse — would sometimes pass, depending on what
    /// the pool last held.
    /// </remarks>
    [TestMethod]
    public void StridePaddingIsNeverCompared()
    {
        using var first = PixelFrameLease.Rent(3, 2, stride: 24, PixelFormat.Rgba8888);
        using var second = PixelFrameLease.Rent(3, 2, stride: 32, PixelFormat.Rgba8888);
        first.Pixels.Fill(0x11);
        second.Pixels.Fill(0xEE);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                TestFrame.Set(first, x, y, 40, 50, 60, 70);
                TestFrame.Set(second, x, y, 40, 50, 60, 70);
            }
        }

        Assert.IsTrue(FrameComparison.AreIdentical(first, second));
        Assert.IsTrue(FrameComparison.Between(first, second).AreIdentical);
    }

    /// <summary>Every pixel differing is still reported exactly, not saturated or short-circuited.</summary>
    [TestMethod]
    public void AWhollyDifferentFrameReportsEveryPixel()
    {
        using var black = TestFrame.Uniform(4, 3, 0, 0, 0, 0);
        using var white = TestFrame.Uniform(4, 3, 255, 255, 255, 255);

        var difference = FrameComparison.Between(white, black);

        Assert.AreEqual(12L, difference.DifferingPixels);
        Assert.AreEqual(1d, difference.DifferingFraction);
        Assert.AreEqual(255, difference.MaxChannelDifference);
        Assert.AreEqual(255L * 4L * 12L, difference.ChannelDifferenceSum);
        Assert.AreEqual(255d, difference.MeanChannelDifference, 1e-12d);
        Assert.AreEqual(0, difference.FirstDifferenceX);
        Assert.AreEqual(0, difference.FirstDifferenceY);
        Assert.AreEqual(4, difference.BoundsWidth);
        Assert.AreEqual(3, difference.BoundsHeight);
    }

    /// <summary>The comparison is symmetric in magnitude, since the channel deltas are absolute.</summary>
    [TestMethod]
    public void SwappingTheOperandsChangesNothingButWhichIsCalledTheReference()
    {
        using var first = TestFrame.Uniform(3, 3, 10, 10, 10);
        using var second = TestFrame.Uniform(3, 3, 10, 10, 10);
        TestFrame.Set(second, 1, 1, 10, 40, 10);

        var forward = FrameComparison.Between(first, second);
        var backward = FrameComparison.Between(second, first);

        Assert.AreEqual(forward.DifferingPixels, backward.DifferingPixels);
        Assert.AreEqual(forward.MaxChannelDifference, backward.MaxChannelDifference);
        Assert.AreEqual(forward.ChannelDifferenceSum, backward.ChannelDifferenceSum);
        Assert.AreEqual(forward.FirstDifferenceX, backward.FirstDifferenceX);
        Assert.AreEqual(forward.FirstDifferenceY, backward.FirstDifferenceY);
        Assert.AreEqual(30, forward.MaxChannelDifference);
    }

    [TestMethod]
    public void TheSummaryLineCarriesTheNumbersAnAssertionMessageNeeds()
    {
        using var reference = TestFrame.Uniform(4, 4, 0, 0, 0);
        using var pixels = TestFrame.Uniform(4, 4, 0, 0, 0);
        TestFrame.Set(pixels, 3, 2, 0, 0, 9);

        var summary = FrameComparison.Between(pixels, reference).ToString();

        Assert.Contains("1 of 16 pixels differ", summary);
        Assert.Contains("worst 9 levels", summary);
        Assert.Contains("first at (3, 2)", summary);
    }

    [TestMethod]
    public void NullAndDisposedFramesAreRejected()
    {
        using var frame = TestFrame.Uniform(2, 2, 0, 0, 0);

        Assert.ThrowsExactly<ArgumentNullException>(() => FrameComparison.AreIdentical(null!, frame));
        Assert.ThrowsExactly<ArgumentNullException>(() => FrameComparison.AreIdentical(frame, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => FrameComparison.Between(null!, frame));
        Assert.ThrowsExactly<ArgumentNullException>(() => FrameComparison.Between(frame, null!));

        var disposed = TestFrame.Uniform(2, 2, 0, 0, 0);
        disposed.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => FrameComparison.AreIdentical(disposed, frame));
        Assert.ThrowsExactly<ObjectDisposedException>(() => FrameComparison.Between(disposed, frame));
    }
}
