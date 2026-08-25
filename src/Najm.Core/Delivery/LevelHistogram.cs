namespace Najm.Core;

/// <summary>The exact distribution of one 8-bit channel across a frame.</summary>
/// <remarks>
/// <para>
/// An 8-bit channel takes 256 values, so counting them costs 256 longs and loses nothing: every
/// number below — percentiles included — is the answer the full sorted sample would have given, not
/// an estimate from bucketed data. That is the whole reason this type is a histogram rather than a
/// running summary. It also means a frame of any size measures in one pass with no sort and no
/// per-pixel allocation, which matters at 1080p (2.07 million pixels) and more at 4K.
/// </para>
/// <para>
/// <strong>Percentiles are nearest-rank, and the top bucket is the trap.</strong>
/// <see cref="Percentile"/> returns the smallest level whose cumulative count reaches
/// <c>ceil(p/100 x N)</c> — the value of the sample at that rank, never an interpolation between two
/// levels. <c>Percentile(100)</c> is therefore exactly <see cref="Max"/> and <c>Percentile(0)</c> is
/// exactly <see cref="Min"/>. Two neighbouring definitions are both wrong here and both look right:
/// a <c>floor</c> rank makes <c>Percentile(100)</c> the second-largest level on some frames, and a
/// strict <c>&gt;</c> on the cumulative comparison shifts every answer one level up. The tests pin
/// all three boundaries deliberately.
/// </para>
/// <para>
/// <strong>Why the rank is computed in <see cref="decimal"/>.</strong> <c>p / 100d * N</c> lands a
/// hair above an exact integer for some pairs — the double nearest to 0.29 times 100 is
/// 29.000000000000004 — and a <c>Math.Ceiling</c> over that silently asks for one rank more than the
/// caller meant. Decimal represents the percentages people actually type (90, 99.9, 0.1) exactly, so
/// the rank comes out exact for them and is off by less than a level for anything else.
/// </para>
/// <para>
/// <c>default(LevelHistogram)</c> is a valid empty distribution: <see cref="Count"/> is zero,
/// <see cref="Min"/> and <see cref="Max"/> are zero, and <see cref="Mean"/> is
/// <see cref="double.NaN"/>. A histogram never mutates after <see cref="FrameStats"/> builds
/// it, and the counts it views are not reachable from outside.
/// </para>
/// </remarks>
public readonly struct LevelHistogram
{
    /// <summary>The number of distinct values an 8-bit channel can take.</summary>
    internal const int Levels = 256;

    private readonly long[]? counts;
    private readonly long total;
    private readonly long weightedSum;

    internal LevelHistogram(long[] counts)
    {
        this.counts = counts;
        for (var level = 0; level < Levels; level++)
        {
            total += counts[level];
            weightedSum += counts[level] * level;
        }

        for (var level = 0; level < Levels; level++)
        {
            if (counts[level] != 0)
            {
                Min = (byte)level;
                break;
            }
        }

        for (var level = Levels - 1; level >= 0; level--)
        {
            if (counts[level] != 0)
            {
                Max = (byte)level;
                break;
            }
        }
    }

    /// <summary>Gets how many samples this distribution covers, which is the frame's pixel count.</summary>
    public long Count => total;

    /// <summary>Gets the lowest level that occurs at least once. Zero for an empty distribution.</summary>
    public byte Min { get; }

    /// <summary>Gets the highest level that occurs at least once. Zero for an empty distribution.</summary>
    public byte Max { get; }

    /// <summary>
    /// Gets the arithmetic mean level, or <see cref="double.NaN"/> when there are no samples.
    /// </summary>
    /// <remarks>
    /// It is exact — the sum is accumulated in integers over the histogram, so no float rounding
    /// enters — but it is also the number that hides the thing being looked for. A frame whose
    /// brightest two percent are welded to 255 has the same mean as one that is merely bright;
    /// <see cref="Percentile"/> and <see cref="CountAtOrAbove"/> are what separate them.
    /// </remarks>
    public double Mean => total == 0L ? double.NaN : (double)weightedSum / total;

    /// <summary>Gets how many samples sit at exactly <paramref name="level"/>.</summary>
    public long CountAt(byte level) => counts is null ? 0L : counts[level];

    /// <summary>Gets how many samples sit at or above <paramref name="level"/>.</summary>
    /// <remarks>
    /// <c>CountAtOrAbove(255)</c> is the count of fully saturated samples in this channel — the
    /// number a grading pass asks for first. Note that it is a <em>channel</em> count, not a pixel
    /// count: a pixel clipped in red alone contributes here and does not contribute to
    /// <see cref="FrameStats.ClippedWhitePixels"/>, which requires all three.
    /// </remarks>
    public long CountAtOrAbove(byte level)
    {
        if (counts is null)
        {
            return 0L;
        }

        var running = 0L;

        // int, not the byte the parameter is: a byte index can never reach Levels, so it would
        // wrap 255 -> 0 and never terminate. The compiler catches it as CS0652 rather than as the
        // infinite loop it actually is.
        for (int index = level; index < Levels; index++)
        {
            running += counts[index];
        }

        return running;
    }

    /// <summary>Gets how many samples sit at or below <paramref name="level"/>.</summary>
    public long CountAtOrBelow(byte level)
    {
        if (counts is null)
        {
            return 0L;
        }

        var running = 0L;
        for (var index = 0; index <= level; index++)
        {
            running += counts[index];
        }

        return running;
    }

    /// <summary>Gets the share of samples at or above <paramref name="level"/>, in zero to one.</summary>
    /// <returns>Zero for an empty distribution, so a caller can print it without a guard.</returns>
    public double FractionAtOrAbove(byte level) =>
        total == 0L ? 0d : (double)CountAtOrAbove(level) / total;

    /// <summary>Gets the share of samples at or below <paramref name="level"/>, in zero to one.</summary>
    /// <returns>Zero for an empty distribution.</returns>
    public double FractionAtOrBelow(byte level) =>
        total == 0L ? 0d : (double)CountAtOrBelow(level) / total;

    /// <summary>Gets the level at the given percentile, by nearest rank.</summary>
    /// <param name="percentile">
    /// The percentile to read, from 0 through 100 inclusive. It is a percentage, not a fraction: p90
    /// is <c>Percentile(90)</c>.
    /// </param>
    /// <returns>
    /// The level of the sample at rank <c>ceil(percentile/100 x Count)</c>, or zero when there are no
    /// samples.
    /// </returns>
    /// <remarks>
    /// The result is always a level that actually occurs in the frame, which is the property that
    /// makes it safe to feed straight back into <see cref="CountAtOrAbove"/> or into an exposure
    /// decision. No interpolated percentile has that property.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="percentile"/> is not a finite value in [0, 100].
    /// </exception>
    public byte Percentile(double percentile)
    {
        if (!double.IsFinite(percentile) || percentile < 0d || percentile > 100d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentile),
                percentile,
                "A percentile must be a finite value from 0 through 100.");
        }

        if (counts is null || total == 0L)
        {
            return 0;
        }

        var rank = RankOf(percentile, total);
        var running = 0L;
        for (var level = 0; level < Levels; level++)
        {
            running += counts[level];
            if (running >= rank)
            {
                return (byte)level;
            }
        }

        return Max;
    }

    /// <summary>Gets the median level, which is <c>Percentile(50)</c>.</summary>
    public byte Median() => Percentile(50d);

    /// <summary>Renders the shape of the distribution as one line, for a report or a failure message.</summary>
    public override string ToString() =>
        total == 0L
            ? "empty"
            : $"min {Min}, p50 {Percentile(50d)}, p90 {Percentile(90d)}, p99 {Percentile(99d)}, " +
                $"max {Max}, mean {Mean:0.###}";

    /// <summary>
    /// Converts a percentile into a one-based rank over <paramref name="count"/> samples, clamped so
    /// that every percentile names a real sample.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="SampleStatistics"/> so the two families cannot drift apart on the one
    /// definition that is easy to get subtly wrong.
    /// </remarks>
    internal static long RankOf(double percentile, long count)
    {
        var rank = (long)Math.Ceiling((decimal)percentile * count / 100m);
        return Math.Clamp(rank, 1L, count);
    }
}
