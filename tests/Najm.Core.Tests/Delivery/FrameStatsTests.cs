using Najm.Utils;

namespace Najm.Core.Tests.Delivery;

/// <summary>
/// What a measurement of a frame has to get right: the clipping counts, the percentile boundaries,
/// and the difference between the two brightnesses it reports.
/// </summary>
/// <remarks>
/// The frames below are hand-built and tiny, so every expected number is derivable by hand from the
/// pixels rather than captured from a previous run. A captured expectation would have locked in
/// whatever the arithmetic did on the day, which is precisely the failure mode this type exists to
/// end.
/// </remarks>
[TestClass]
public sealed class FrameStatsTests
{
    /// <summary>
    /// Clipping is a joint question about a pixel, not a marginal one about a channel.
    /// </summary>
    /// <remarks>
    /// The frame below contains a saturated pure red, which puts three counts in red's top bucket
    /// and must put none in the clipped-white population. Getting this wrong is easy and quiet: a
    /// frame full of saturated primaries would report as blown out.
    /// </remarks>
    [TestMethod]
    public void ClippedWhiteCountsPixelsWithEveryChannelAtTheThresholdAndNotSaturatedPrimaries()
    {
        using var frame = TestFrame.FromPixels(
            4,
            4,
            PixelFormat.Rgba8888,
            (255, 255, 255, 255),
            (255, 255, 255, 255),
            (255, 255, 255, 255),
            (255, 254, 255, 255),
            (255, 0, 0, 255),
            (255, 0, 0, 255),
            (255, 0, 0, 255),
            (0, 255, 0, 255),
            (128, 128, 128, 255),
            (128, 128, 128, 255),
            (128, 128, 128, 255),
            (128, 128, 128, 255),
            (0, 0, 0, 255),
            (0, 0, 0, 255),
            (0, 0, 0, 255),
            (0, 1, 0, 255));

        var stats = FrameStats.Of(frame);

        Assert.AreEqual(3L, stats.ClippedWhitePixels(), "Only the three that are 255 in all three.");
        Assert.AreEqual(
            4L,
            stats.ClippedWhitePixels(254),
            "Lowering the threshold to 254 pulls in the near-white pixel, which is the reason the " +
            "threshold is a query parameter.");
        Assert.AreEqual(3L / 16d, stats.ClippedWhiteFraction(), 1e-12d);

        Assert.AreEqual(
            7L,
            stats.CountAtOrAbove(FrameChannel.Red, 255),
            "Red's own top bucket holds the four whites and the three saturated reds — a marginal " +
            "count, not a clipping count.");
    }

    [TestMethod]
    public void CrushedBlackCountsPixelsWithEveryChannelAtOrUnderTheThreshold()
    {
        using var frame = TestFrame.FromPixels(
            2,
            3,
            PixelFormat.Rgba8888,
            (0, 0, 0, 255),
            (0, 0, 0, 255),
            (0, 1, 0, 255),
            (0, 0, 40, 255),
            (255, 255, 255, 255),
            (12, 9, 3, 255));

        var stats = FrameStats.Of(frame);

        Assert.AreEqual(2L, stats.CrushedBlackPixels(), "Exactly the two that are zero everywhere.");
        Assert.AreEqual(3L, stats.CrushedBlackPixels(1), "Raising the floor to 1 pulls in (0, 1, 0).");
        Assert.AreEqual(
            5L,
            stats.CrushedBlackPixels(40),
            "Raising it to 40 pulls in (0, 0, 40) and (12, 9, 3) as well.");
        Assert.AreEqual(2L / 6d, stats.CrushedBlackFraction(), 1e-12d);
    }

    /// <summary>The two ends of the quantile range, which are the two an implementation gets wrong.</summary>
    /// <remarks>
    /// <c>Percentile(c, 1.0)</c> must be the maximum. An implementation comparing the running total
    /// with a strict <c>&gt;</c> never satisfies it at rank <c>N</c>, falls out of the loop, and
    /// returns 255 — a level no pixel in this frame has.
    /// </remarks>
    [TestMethod]
    public void QuantileZeroIsTheMinimumAndQuantileOneIsTheMaximum()
    {
        using var frame = TestFrame.Greys(10, 20, 30, 40, 50, 60, 70, 80);
        var stats = FrameStats.Of(frame);

        Assert.AreEqual((byte)10, stats.Minimum(FrameChannel.Luma));
        Assert.AreEqual((byte)80, stats.Maximum(FrameChannel.Luma));
        Assert.AreEqual(stats.Minimum(FrameChannel.Luma), stats.Percentile(FrameChannel.Luma, 0d));
        Assert.AreEqual(
            stats.Maximum(FrameChannel.Luma),
            stats.Percentile(FrameChannel.Luma, 1d),
            "A strict '>' on the cumulative count falls off the end here and answers 255.");
    }

    /// <summary>
    /// One expectation that both neighbouring wrong rank definitions fail.
    /// </summary>
    /// <remarks>
    /// Eight samples at eight distinct levels, queried at the 0.3 quantile. The correct rank is
    /// <c>ceil(2.4) = 3</c> and the third-smallest level is 30. A <c>floor</c> rank asks for rank 2
    /// and answers 20; a strict <c>&gt;</c> walks one bucket further and answers 40.
    /// </remarks>
    [TestMethod]
    public void ANonIntegerRankRoundsUpToTheSampleThatContainsIt()
    {
        using var frame = TestFrame.Greys(10, 20, 30, 40, 50, 60, 70, 80);
        var stats = FrameStats.Of(frame);

        Assert.AreEqual(
            (byte)30,
            stats.Percentile(FrameChannel.Green, 0.3d),
            "Rank ceil(0.3 x 8) = 3; a floor rank answers 20 and a strict '>' answers 40.");
        Assert.AreEqual((byte)80, stats.Percentile(FrameChannel.Green, 0.9d), "Rank ceil(7.2) = 8.");
        Assert.AreEqual((byte)40, stats.Percentile(FrameChannel.Green, 0.5d));
    }

    /// <summary>
    /// The rank is exact for quantiles that binary floating point would round the wrong way.
    /// </summary>
    /// <remarks>
    /// A hundred pixels whose red runs 0 to 99, queried at the 0.07 quantile. Exactly seven percent
    /// of a hundred is rank 7, which is level 6. In <c>double</c>, <c>0.07 * 100</c> is
    /// 7.000000000000001, so a naive ceiling asks for rank 8 and the answer moves to level 7 — a
    /// silent off-by-one on a perfectly ordinary query.
    /// </remarks>
    [TestMethod]
    public void TheRankIsExactForQuantilesThatBinaryFloatingPointWouldRoundUp()
    {
        var pixels = new (byte, byte, byte, byte)[100];
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = ((byte)index, 0, 0, 255);
        }

        using var frame = TestFrame.FromPixels(10, 10, PixelFormat.Rgba8888, pixels);
        var stats = FrameStats.Of(frame);

        Assert.AreEqual(
            (byte)6,
            stats.Percentile(FrameChannel.Red, 0.07d),
            "Seven percent of a hundred pixels is rank 7, which is level 6.");
        Assert.AreEqual((byte)13, stats.Percentile(FrameChannel.Red, 0.14d));
        Assert.AreEqual((byte)99, stats.Percentile(FrameChannel.Red, 1d));
        Assert.AreEqual((byte)0, stats.Percentile(FrameChannel.Red, 0d));
    }

    /// <summary>
    /// Luma is Rec. 709 over the encoded bytes; relative luminance is Rec. 709 over linear light.
    /// They are different numbers for the same pixel, and confusing them is the failure this pins.
    /// </summary>
    [TestMethod]
    public void LumaIsEncodedAndRelativeLuminanceIsLinearAndTheyDisagree()
    {
        using var green = TestFrame.Uniform(4, 4, 0, 255, 0);
        var greenStats = FrameStats.Of(green);

        Assert.AreEqual(
            (byte)182,
            greenStats.Maximum(FrameChannel.Luma),
            "0.7152 x 255 = 182.4, rounded to 182 — luma reads the encoded byte straight.");
        Assert.AreEqual(
            0.7152d,
            greenStats.MeanRelativeLuminance,
            1e-6d,
            "Level 255 decodes to linear 1.0, so the luminance is the green weight itself.");

        using var midGrey = TestFrame.Uniform(4, 4, 128, 128, 128);
        var greyStats = FrameStats.Of(midGrey);

        Assert.AreEqual(
            (byte)128,
            greyStats.Maximum(FrameChannel.Luma),
            "The weights sum to one, so a neutral grey's luma is its own level.");
        Assert.AreEqual(
            Color.SrgbToLinear(128f / 255f),
            greyStats.MeanRelativeLuminance,
            1e-6d,
            "Mid grey is a fifth of the light, not half of it. This is the whole reason the two " +
            "quantities are reported separately.");
        Assert.IsGreaterThan(
            0.25d,
            (greyStats.Maximum(FrameChannel.Luma) / 255d) - greyStats.MeanRelativeLuminance,
            "Encoded and linear must not be interchangeable: mid grey is 0.502 as a code value and " +
            "0.216 as light, and a report that confused them would be wrong by more than a stop.");
    }

    /// <summary>
    /// The realized dynamic range, and its ceiling for an 8-bit sRGB frame.
    /// </summary>
    /// <remarks>
    /// Level 1 decodes to 1/255/12.92 = 0.0003035 and level 255 to 1.0, so the widest range an
    /// 8-bit sRGB frame can report is log2(1/0.0003035) = 11.69 stops. Reaching it means the
    /// encoding, not the scene, set the limit.
    /// </remarks>
    [TestMethod]
    public void DynamicRangeSpansTheBrightestPixelToTheDarkestLitOne()
    {
        using var frame = TestFrame.Greys(0, 1, 128, 255);
        var stats = FrameStats.Of(frame);

        var darkestLit = (double)Color.SrgbToLinear(1f / 255f);
        Assert.AreEqual(0d, stats.MinimumRelativeLuminance, 1e-12d, "The black pixel is the minimum.");
        Assert.AreEqual(1d, stats.MaximumRelativeLuminance, 1e-6d);
        Assert.AreEqual(
            Math.Log2(1d / darkestLit),
            stats.DynamicRangeStops,
            1e-6d,
            "Absolute black is excluded, or every frame containing any would report infinity.");
        Assert.AreEqual(11.69d, stats.DynamicRangeStops, 0.01d, "The 8-bit sRGB ceiling.");
    }

    [TestMethod]
    public void AFrameWithNoLitPixelReportsNoDynamicRangeRatherThanInfinity()
    {
        using var frame = TestFrame.Uniform(3, 3, 0, 0, 0);
        var stats = FrameStats.Of(frame);

        Assert.AreEqual(0d, stats.DynamicRangeStops);
        Assert.AreEqual(0d, stats.MeanRelativeLuminance);
        Assert.AreEqual(9L, stats.CrushedBlackPixels());
    }

    /// <summary>
    /// The 8-bit analogue of "is anything NaN or negative": a premultiplied pixel brighter than its
    /// own alpha, which no correct blend produces.
    /// </summary>
    [TestMethod]
    public void PremultipliedPixelsBrighterThanTheirAlphaAreCountedAsInvalid()
    {
        using var premultiplied = TestFrame.FromPixels(
            2,
            2,
            PixelFormat.Rgba8888Premul,
            (200, 0, 0, 100),
            (50, 50, 50, 100),
            (0, 0, 0, 0),
            (255, 255, 255, 255));

        Assert.AreEqual(1L, FrameStats.Of(premultiplied).InvalidPixels);

        // The same bytes tagged straight are all legal, because straight alpha imposes no such bound.
        using var straight = TestFrame.FromPixels(
            2,
            2,
            PixelFormat.Rgba8888,
            (200, 0, 0, 100),
            (50, 50, 50, 100),
            (0, 0, 0, 0),
            (255, 255, 255, 255));

        Assert.AreEqual(0L, FrameStats.Of(straight).InvalidPixels);
    }

    [TestMethod]
    public void OpacityIsReportedFromTheAlphaHistogram()
    {
        using var opaque = TestFrame.Uniform(2, 2, 10, 20, 30);
        Assert.IsTrue(FrameStats.Of(opaque).AllPixelsOpaque);

        using var translucent = TestFrame.FromPixels(
            2,
            1,
            PixelFormat.Rgba8888,
            (10, 20, 30, 255),
            (10, 20, 30, 254));
        var stats = FrameStats.Of(translucent);

        Assert.IsFalse(stats.AllPixelsOpaque);
        Assert.AreEqual(1L, stats.CountAtLevel(FrameChannel.Alpha, 254));
    }

    /// <summary>Channels are named by colour, not by byte offset.</summary>
    /// <remarks>
    /// The same logical image stored RGBA and BGRA must measure identically. If the mapping were
    /// wrong, every statistic would still look plausible — only red and blue would have quietly
    /// swapped, which no downstream number could reveal.
    /// </remarks>
    [TestMethod]
    public void ChannelsAreLogicalSoByteOrderDoesNotChangeTheMeasurement()
    {
        using var rgba = TestFrame.FromPixels(
            2,
            1,
            PixelFormat.Rgba8888,
            (200, 100, 50, 255),
            (10, 20, 30, 255));
        using var bgra = TestFrame.FromPixels(
            2,
            1,
            PixelFormat.Bgra8888Premul,
            (200, 100, 50, 255),
            (10, 20, 30, 255));

        var fromRgba = FrameStats.Of(rgba);
        var fromBgra = FrameStats.Of(bgra);

        Assert.AreEqual(fromRgba.Maximum(FrameChannel.Red), fromBgra.Maximum(FrameChannel.Red));
        Assert.AreEqual((byte)200, fromBgra.Maximum(FrameChannel.Red));
        Assert.AreEqual((byte)50, fromBgra.Maximum(FrameChannel.Blue));
        Assert.AreEqual(fromRgba.Mean(FrameChannel.Luma), fromBgra.Mean(FrameChannel.Luma), 1e-12d);
        Assert.AreEqual(fromRgba.MeanRelativeLuminance, fromBgra.MeanRelativeLuminance, 1e-12d);
    }

    /// <summary>Stride padding is not image data and must not enter any statistic.</summary>
    [TestMethod]
    public void StridePaddingIsNotMeasured()
    {
        using var lease = PixelFrameLease.Rent(4, 3, stride: 40, PixelFormat.Rgba8888);
        lease.Pixels.Fill(255);
        for (var y = 0; y < 3; y++)
        {
            lease.Row(y).Clear();
            for (var x = 0; x < 4; x++)
            {
                TestFrame.Set(lease, x, y, 0, 0, 0);
            }
        }

        var stats = FrameStats.Of(lease);

        Assert.AreEqual(12L, stats.PixelCount, "The padding is not pixels.");
        Assert.AreEqual(0L, stats.ClippedWhitePixels(), "The padding bytes are all 255 and must not count.");
        Assert.AreEqual(12L, stats.CrushedBlackPixels());
        Assert.AreEqual(0d, stats.Mean(FrameChannel.Red));
    }

    [TestMethod]
    public void HistogramsCoverEveryPixelExactlyOnce()
    {
        using var frame = TestFrame.Greys(0, 1, 2, 3, 250, 251, 252, 255);
        var stats = FrameStats.Of(frame);

        foreach (var channel in Enum.GetValues<FrameChannel>())
        {
            var total = 0L;
            foreach (var count in stats.Histogram(channel))
            {
                total += count;
            }

            Assert.AreEqual(stats.PixelCount, total, $"{channel}'s histogram must account for every pixel.");
            Assert.AreEqual(
                stats.PixelCount,
                stats.CountAtOrAbove(channel, 0),
                $"{channel} must count every pixel at or above zero.");
            Assert.AreEqual(
                stats.PixelCount,
                stats.CountAtOrBelow(channel, 255),
                $"{channel} must count every pixel at or below 255.");
        }
    }

    /// <summary>An instance is reusable, and re-measuring replaces rather than accumulates.</summary>
    [TestMethod]
    public void RemeasuringReplacesThePreviousFrameEntirely()
    {
        var stats = new FrameStats();
        Assert.IsFalse(stats.HasMeasurement);

        using var white = TestFrame.Uniform(4, 4, 255, 255, 255);
        stats.Measure(white);
        Assert.AreEqual(16L, stats.ClippedWhitePixels());

        using var black = TestFrame.Uniform(2, 2, 0, 0, 0);
        stats.Measure(black);

        Assert.IsTrue(stats.HasMeasurement);
        Assert.AreEqual(4L, stats.PixelCount, "The second measurement's shape, not the first's.");
        Assert.AreEqual(0L, stats.ClippedWhitePixels(), "Nothing may survive from the previous frame.");
        Assert.AreEqual(4L, stats.CrushedBlackPixels());
        Assert.AreEqual(0d, stats.MeanRelativeLuminance);
    }

    [TestMethod]
    public void ReadingAnUnmeasuredInstanceFailsLoudlyRatherThanReturningZero()
    {
        var stats = new FrameStats();

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = stats.Width);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = stats.PixelCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = stats.MeanRelativeLuminance);
        Assert.ThrowsExactly<InvalidOperationException>(() => stats.ClippedWhitePixels());
        Assert.ThrowsExactly<InvalidOperationException>(() => stats.Percentile(FrameChannel.Red, 0.5d));
        Assert.ThrowsExactly<InvalidOperationException>(() => stats.Describe());
    }

    [TestMethod]
    public void MalformedMeasurementRequestsAreRejected()
    {
        var pixels = new byte[4 * 4 * 4];
        var stats = new FrameStats();

        Assert.ThrowsExactly<ArgumentNullException>(() => stats.Measure(null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            stats.Measure(pixels, 0, 4, 16, PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            stats.Measure(pixels, 4, 4, 15, PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentException>(() =>
            stats.Measure(pixels, 4, 4, 16, (PixelFormat)99));
        Assert.ThrowsExactly<ArgumentException>(() =>
            stats.Measure(new byte[8], 4, 4, 16, PixelFormat.Rgba8888));

        using var frame = TestFrame.Uniform(2, 2, 1, 2, 3);
        var measured = FrameStats.Of(frame);
        Assert.ThrowsExactly<ArgumentException>(() => measured.Histogram((FrameChannel)42));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            measured.Percentile(FrameChannel.Red, 1.0001d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            measured.Percentile(FrameChannel.Red, double.NaN));
    }

    /// <summary>The report has to carry the numbers a grading log is kept for.</summary>
    [TestMethod]
    public void TheReportNamesTheFrameAndItsClipping()
    {
        using var frame = TestFrame.Uniform(6, 4, 255, 255, 255);
        var description = FrameStats.Of(frame).Describe();

        Assert.Contains("6×4", description);
        Assert.Contains("24 px", description);
        Assert.Contains("clipped white", description);
        Assert.Contains("100.0000%", description, "Every pixel of an all-white frame is clipped.");
        Assert.Contains("stops", description);
    }
}
