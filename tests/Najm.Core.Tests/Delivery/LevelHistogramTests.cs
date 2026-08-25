namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The percentile and cumulative-count arithmetic, pinned at the boundaries where a wrong
/// definition still looks right.
/// </summary>
/// <remarks>
/// <para>
/// Every test here is chosen so that at least one plausible mis-implementation produces a different
/// answer. A histogram that is merely "about right" is worse than none: the numbers it produces get
/// pasted into a report and argued from, and nothing downstream can tell that the top bucket was
/// dropped.
/// </para>
/// <para>
/// The three mis-implementations under guard are named in <see cref="LevelHistogram"/>'s own
/// remarks — a <c>floor</c> rank, a strict <c>&gt;</c> on the cumulative comparison, and a rank
/// computed in binary floating point — and each has a test below that fails under it and passes
/// under the real one.
/// </para>
/// </remarks>
[TestClass]
public sealed class LevelHistogramTests
{
    [TestMethod]
    public void ADistributionReportsItsExtremesAndItsExactMean()
    {
        var histogram = Histogram((10, 1), (20, 2), (90, 1));

        Assert.AreEqual(4L, histogram.Count);
        Assert.AreEqual((byte)10, histogram.Min);
        Assert.AreEqual((byte)90, histogram.Max);
        Assert.AreEqual(35d, histogram.Mean, "10 + 20 + 20 + 90 over four samples is exactly 35.");
        Assert.AreEqual(1L, histogram.CountAt(10));
        Assert.AreEqual(2L, histogram.CountAt(20));
        Assert.AreEqual(0L, histogram.CountAt(21));
    }

    /// <summary>
    /// The two ends of the percentile range, which are the two an implementation gets wrong.
    /// </summary>
    /// <remarks>
    /// <c>Percentile(0)</c> is the minimum because rank zero names no sample and clamps up to the
    /// first; <c>Percentile(100)</c> is the maximum because rank <c>N</c> names the last one. An
    /// implementation that indexes off the end, or that compares the running total with a strict
    /// <c>&gt;</c>, loses the top bucket and returns something else entirely.
    /// </remarks>
    [TestMethod]
    public void PercentileZeroIsTheMinimumAndPercentileOneHundredIsTheMaximum()
    {
        var histogram = Histogram((10, 1), (20, 1), (30, 1), (40, 1), (50, 1), (60, 1), (70, 1), (80, 1));

        Assert.AreEqual(histogram.Min, histogram.Percentile(0d));
        Assert.AreEqual((byte)10, histogram.Percentile(0d));
        Assert.AreEqual(histogram.Max, histogram.Percentile(100d));
        Assert.AreEqual((byte)80, histogram.Percentile(100d));
    }

    /// <summary>
    /// One assertion that both neighbouring wrong definitions fail, and the arithmetic that shows
    /// why.
    /// </summary>
    /// <remarks>
    /// Eight samples at eight distinct levels, queried at p30. The correct rank is
    /// <c>ceil(0.30 x 8) = ceil(2.4) = 3</c>, and the third-smallest sample is level 30. A
    /// <c>floor</c> rank asks for rank 2 and answers 20. A strict <c>&gt;</c> on the cumulative
    /// comparison walks one bucket past the one that reaches rank 3 and answers 40. Only the
    /// nearest-rank definition answers 30, so this single expectation is a three-way discriminator.
    /// </remarks>
    [TestMethod]
    public void ANonIntegerRankRoundsUpToTheSampleThatContainsIt()
    {
        var histogram = Histogram((10, 1), (20, 1), (30, 1), (40, 1), (50, 1), (60, 1), (70, 1), (80, 1));

        Assert.AreEqual(
            (byte)30,
            histogram.Percentile(30d),
            "p30 of eight samples is rank ceil(2.4) = 3; a floor rank would answer 20 and a strict " +
            "'>' on the cumulative count would answer 40.");

        // p90 of eight samples is rank ceil(7.2) = 8, which is the last sample. A floor rank would
        // answer 70 here, so the top of the range is guarded twice by different arithmetic.
        Assert.AreEqual((byte)80, histogram.Percentile(90d));
    }

    /// <summary>
    /// The rank is computed in decimal, and this is a case where computing it in double would move
    /// the answer.
    /// </summary>
    /// <remarks>
    /// 375 samples queried at p8.8. In exact arithmetic the rank is <c>8.8 x 375 / 100 = 33</c>
    /// precisely. In binary floating point <c>8.8 * 375</c> is 3300.0000000000005, so the rank
    /// ceilings to 34 and the answer moves one level up. The distribution below places one sample at
    /// each level from 0 to 254 so that rank <c>k</c> is level <c>k - 1</c> and the two ranks name
    /// visibly different levels.
    /// </remarks>
    [TestMethod]
    public void TheRankIsExactForPercentilesThatBinaryFloatingPointWouldRoundUp()
    {
        var counts = new long[LevelHistogram.Levels];
        for (var level = 0; level < 255; level++)
        {
            counts[level] = 1L;
        }

        counts[255] = 120L;
        var histogram = new LevelHistogram(counts);
        Assert.AreEqual(375L, histogram.Count);

        Assert.AreEqual(33L, LevelHistogram.RankOf(8.8d, 375L), "8.8% of 375 is exactly 33.");
        Assert.AreEqual(
            (byte)32,
            histogram.Percentile(8.8d),
            "Rank 33 of this distribution is level 32; a double-computed rank of 34 would answer 33.");
    }

    [TestMethod]
    public void RanksClampSoThatEveryPercentileNamesARealSample()
    {
        Assert.AreEqual(1L, LevelHistogram.RankOf(0d, 100L), "Rank zero is not a sample; it clamps up.");
        Assert.AreEqual(100L, LevelHistogram.RankOf(100d, 100L));
        Assert.AreEqual(50L, LevelHistogram.RankOf(50d, 100L));
        Assert.AreEqual(1L, LevelHistogram.RankOf(0.4d, 100L), "Ceiling, so any positive share reaches rank one.");
    }

    /// <summary>
    /// A distribution shaped like a hot frame: a large flat body and a small clipped tail.
    /// </summary>
    /// <remarks>
    /// This is the shape the whole type exists for. The mean is dragged around by the body and says
    /// nothing about the tail; the p99 is the number that finds it.
    /// </remarks>
    [TestMethod]
    public void APercentileFindsAClippedTailThatTheMeanHides()
    {
        var histogram = Histogram((100, 98), (255, 2));

        Assert.AreEqual((byte)100, histogram.Percentile(90d));
        Assert.AreEqual((byte)100, histogram.Percentile(98d), "Rank 98 is the last body sample.");
        Assert.AreEqual((byte)255, histogram.Percentile(99d), "Rank 99 has crossed into the tail.");
        Assert.AreEqual(2L, histogram.CountAtOrAbove(255));
        Assert.AreEqual(0.02d, histogram.FractionAtOrAbove(255), 1e-12d);
        Assert.AreEqual((byte)100, histogram.Median());
    }

    /// <summary>
    /// The cumulative counts at both ends of the level range.
    /// </summary>
    /// <remarks>
    /// <c>CountAtOrAbove(255)</c> is the direct regression test for the loop index this type shipped
    /// with: a <c>byte</c> counter can never reach 256, so it wrapped 255 back to 0 and spun. A test
    /// that only ever asked for a middle level would have passed against it forever.
    /// </remarks>
    [TestMethod]
    public void CumulativeCountsAreInclusiveAndSurviveBothEndsOfTheLevelRange()
    {
        var histogram = Histogram((0, 5), (1, 3), (128, 7), (254, 2), (255, 4));

        Assert.AreEqual(21L, histogram.Count);
        Assert.AreEqual(21L, histogram.CountAtOrAbove(0), "Every sample is at or above zero.");
        Assert.AreEqual(21L, histogram.CountAtOrBelow(255), "Every sample is at or below 255.");
        Assert.AreEqual(4L, histogram.CountAtOrAbove(255), "Only the top bucket.");
        Assert.AreEqual(5L, histogram.CountAtOrBelow(0), "Only the bottom bucket.");
        Assert.AreEqual(6L, histogram.CountAtOrAbove(254));
        Assert.AreEqual(8L, histogram.CountAtOrBelow(1));

        // The two halves partition the sample exactly, at every split point.
        for (var level = 1; level < LevelHistogram.Levels; level++)
        {
            Assert.AreEqual(
                histogram.Count,
                histogram.CountAtOrBelow((byte)(level - 1)) + histogram.CountAtOrAbove((byte)level),
                $"The split at level {level} must lose nothing and double-count nothing.");
        }

        Assert.AreEqual(1d, histogram.FractionAtOrAbove(0), 1e-12d);
        Assert.AreEqual(1d, histogram.FractionAtOrBelow(255), 1e-12d);
    }

    [TestMethod]
    public void ASingleSampleIsItsOwnEveryPercentile()
    {
        var histogram = Histogram((77, 1));

        Assert.AreEqual((byte)77, histogram.Percentile(0d));
        Assert.AreEqual((byte)77, histogram.Percentile(50d));
        Assert.AreEqual((byte)77, histogram.Percentile(100d));
        Assert.AreEqual((byte)77, histogram.Min);
        Assert.AreEqual((byte)77, histogram.Max);
        Assert.AreEqual(77d, histogram.Mean);
    }

    /// <summary>The default value is a valid empty distribution rather than a trap.</summary>
    [TestMethod]
    public void ADefaultHistogramIsEmptyAndAnswersWithoutThrowing()
    {
        var histogram = default(LevelHistogram);

        Assert.AreEqual(0L, histogram.Count);
        Assert.AreEqual((byte)0, histogram.Min);
        Assert.AreEqual((byte)0, histogram.Max);
        Assert.IsTrue(double.IsNaN(histogram.Mean), "No samples means no mean, not zero.");
        Assert.AreEqual(0L, histogram.CountAt(128));
        Assert.AreEqual(0L, histogram.CountAtOrAbove(0));
        Assert.AreEqual(0L, histogram.CountAtOrBelow(255));
        Assert.AreEqual(0d, histogram.FractionAtOrAbove(0));
        Assert.AreEqual(0d, histogram.FractionAtOrBelow(255));
        Assert.AreEqual((byte)0, histogram.Percentile(50d));
        Assert.AreEqual("empty", histogram.ToString());
    }

    [TestMethod]
    public void PercentilesOutsideTheHundredPointScaleAreRejected()
    {
        var histogram = Histogram((10, 1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.Percentile(-0.001d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.Percentile(100.001d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.Percentile(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => histogram.Percentile(double.PositiveInfinity));
    }

    /// <summary>
    /// The one-line summary names the shape, so a failing assertion says what the frame looked like.
    /// </summary>
    [TestMethod]
    public void TheSummaryLineCarriesTheShapeOfTheDistribution()
    {
        var summary = Histogram((100, 98), (255, 2)).ToString();

        Assert.Contains("min 100", summary);
        Assert.Contains("max 255", summary);
        Assert.Contains("p99 255", summary, "The tail has to survive into the summary line.");
    }

    private static LevelHistogram Histogram(params (int Level, long Count)[] entries)
    {
        var counts = new long[LevelHistogram.Levels];
        foreach (var (level, count) in entries)
        {
            counts[level] += count;
        }

        return new LevelHistogram(counts);
    }
}
