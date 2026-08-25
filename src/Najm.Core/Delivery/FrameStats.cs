using System.Globalization;
using System.Text;
using Najm.Utils;

namespace Najm.Core;

/// <summary>
/// Describes one rendered frame numerically: how much of it is clipped, how its levels are
/// distributed, and how much dynamic range it actually carries.
/// </summary>
/// <remarks>
/// <para>
/// This is the measuring half of a grading loop — render, measure, look. Without it the only
/// available report on a frame is "it seems brighter", and the two productions that needed a real
/// answer each ended up decoding pixels by hand outside the engine. It sits beside
/// <see cref="IFrameSink"/> because a frame's pixels are what the delivery seam already speaks in;
/// it is in <c>Najm.Core</c> rather than a backend because arithmetic over a decoded
/// <see cref="PixelFrameLease"/> is backend-neutral. Only <em>obtaining</em> the buffer — decoding
/// a PNG on disk, for instance — needs a backend, and that lives in <c>Najm.Skia</c>.
/// </para>
/// <para>
/// <b>Percentiles, not means.</b> A mean says almost nothing about a frame whose interesting
/// content is a bright core on a dark field: dropping the exposure by four stops barely moves it,
/// and doubling the clipped area barely moves it either. The questions that decide a grade are
/// "what fraction of pixels is at the top" and "where is the 90th percentile", both of which this
/// answers exactly rather than by estimate, because 8-bit values make a 256-bucket histogram a
/// complete record of the distribution rather than a summary of it.
/// </para>
/// <para>
/// <b>Two brightnesses, and they are not interchangeable.</b> The bytes in a frame are sRGB-encoded
/// — non-linear. <see cref="FrameChannel.Luma"/> applies the Rec. 709 weights to those encoded
/// bytes, which is what luma (<c>Y′</c>) means and is the right quantity for "what code level is
/// the 90th percentile" and for comparing against a 0–255 display scale.
/// <see cref="MeanRelativeLuminance"/> and its neighbours first decode each channel through the
/// sRGB transfer function and then weight, which is photometric relative luminance (<c>Y</c>) and
/// is the only one of the two you may average, ratio, or take a base-2 logarithm of. Averaging
/// encoded values overstates the mean brightness of a dark frame substantially; reading a linear
/// value as a code level understates midtones by roughly the same amount. They are named apart on
/// purpose, because getting this wrong silently poisons every number built on top of it.
/// </para>
/// <para>
/// <b>Premultiplied frames are measured as stored.</b> For
/// <see cref="PixelFormat.Rgba8888Premul"/> and <see cref="PixelFormat.Bgra8888Premul"/> the colour
/// bytes already carry alpha, and nothing here divides it back out — an unpremultiply is lossy at
/// low alpha and would invent precision. The statistics therefore describe the frame as it composites
/// over black, which for the opaque frames a render delivers is the same thing;
/// <see cref="AllPixelsOpaque"/> is the one-line check that it is. <see cref="InvalidPixels"/>
/// covers the 8-bit analogue of "is anything NaN or negative": a byte cannot be either, but a
/// premultiplied pixel whose colour exceeds its alpha is just as impossible, and it is the shape
/// that a bad blend or a mis-tagged buffer actually takes here.
/// </para>
/// <para>
/// <b>Cost.</b> One pass over the pixels and no per-pixel allocation. An instance owns its
/// histograms, so re-measuring into the same instance allocates nothing at all and a caller may
/// keep one for a whole run. This is still a diagnostic: nothing in the render path constructs or
/// calls it, and it should stay that way — a full-frame reduction per frame is a real cost that a
/// render should pay only when someone asked for the numbers.
/// </para>
/// </remarks>
public sealed class FrameStats
{
    /// <summary>Buckets per histogram — one per 8-bit level, so the distribution is exact.</summary>
    private const int Levels = 256;

    private const int ChannelCount = 7;

    /// <summary>Rec. 709 luma/luminance weights, used on encoded and linear values respectively.</summary>
    private const double RedWeight = 0.2126d;
    private const double GreenWeight = 0.7152d;
    private const double BlueWeight = 0.0722d;

    /// <summary>
    /// The sRGB electro-optical transfer function, tabulated. A frame has at most 256 distinct
    /// values per channel, so the decode is a lookup rather than a per-pixel <c>pow</c>. It is built
    /// from <see cref="Color.SrgbToLinear(float)"/> rather than from a private copy of the curve so
    /// that a measurement and a colour conversion elsewhere in the engine can never disagree about
    /// what sRGB is.
    /// </summary>
    private static readonly double[] LinearOfLevel = BuildLinearTable();

    /// <summary>The channels <see cref="Describe"/> tabulates, in reading order.</summary>
    private static readonly FrameChannel[] DescribedChannels =
    [
        FrameChannel.Red,
        FrameChannel.Green,
        FrameChannel.Blue,
        FrameChannel.Alpha,
        FrameChannel.Luma,
    ];

    /// <summary>All seven histograms end to end, so an instance owns exactly one array.</summary>
    private readonly int[] histograms = new int[ChannelCount * Levels];

    private int measuredWidth;
    private int measuredHeight;
    private PixelFormat measuredFormat;
    private long invalidPixels;
    private double luminanceSum;
    private double minimumLuminance;
    private double minimumPositiveLuminance;
    private double maximumLuminance;

    /// <summary>Creates an empty accumulator. Call <see cref="Measure(PixelFrameLease)"/> to fill it.</summary>
    /// <remarks>
    /// The histograms are allocated here, once. Reuse the instance across frames when measuring a
    /// sequence — <see cref="Measure(PixelFrameLease)"/> resets it, and the reuse is what keeps a
    /// per-frame measurement allocation-free.
    /// </remarks>
    public FrameStats()
    {
    }

    /// <summary>Gets whether a frame has been measured into this instance yet.</summary>
    public bool HasMeasurement { get; private set; }

    /// <summary>Gets the measured frame's width in pixels.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public int Width
    {
        get
        {
            EnsureMeasured();
            return measuredWidth;
        }
    }

    /// <summary>Gets the measured frame's height in pixels.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public int Height
    {
        get
        {
            EnsureMeasured();
            return measuredHeight;
        }
    }

    /// <summary>Gets the byte and alpha layout the measured frame was stored in.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public PixelFormat Format
    {
        get
        {
            EnsureMeasured();
            return measuredFormat;
        }
    }

    /// <summary>Gets the number of pixels measured, which is width times height.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long PixelCount
    {
        get
        {
            EnsureMeasured();
            return (long)measuredWidth * measuredHeight;
        }
    }

    /// <summary>Gets whether every pixel's alpha is 255.</summary>
    /// <remarks>
    /// Worth checking before trusting any colour statistic on a premultiplied frame: when this is
    /// true, premultiplied and straight bytes are the same bytes and the distinction stops
    /// mattering. A render into an opaque target satisfies it; a capture of a layer does not have to.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public bool AllPixelsOpaque
    {
        get
        {
            EnsureMeasured();
            return CountAtLevel(FrameChannel.Alpha, 255) == PixelCount;
        }
    }

    /// <summary>
    /// Gets the number of pixels that cannot legally exist: a premultiplied pixel with a colour
    /// channel above its alpha. Always zero for a straight-alpha format, where no byte combination
    /// is invalid.
    /// </summary>
    /// <remarks>
    /// This is the frame-integrity check that a float pipeline would spell "is anything NaN or
    /// negative". An 8-bit buffer admits neither, but it does admit this, and it means the same
    /// thing when it fires: something upstream wrote pixels that no correct blend produces, most
    /// often a buffer tagged with the wrong <see cref="PixelFormat"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long InvalidPixels
    {
        get
        {
            EnsureMeasured();
            return invalidPixels;
        }
    }

    /// <summary>
    /// Gets the mean photometric relative luminance in <c>[0, 1]</c> — channels decoded through the
    /// sRGB transfer function, then Rec. 709 weighted.
    /// </summary>
    /// <remarks>
    /// This, not the mean of <see cref="FrameChannel.Luma"/>, is the frame's average light output.
    /// Use it when the next step is a ratio, a stop count, or a comparison against a physical
    /// target; use the luma percentiles when the next step is "what number will I see in a colour
    /// picker".
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double MeanRelativeLuminance
    {
        get
        {
            EnsureMeasured();
            return luminanceSum / PixelCount;
        }
    }

    /// <summary>Gets the darkest pixel's relative luminance in <c>[0, 1]</c>.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double MinimumRelativeLuminance
    {
        get
        {
            EnsureMeasured();
            return minimumLuminance;
        }
    }

    /// <summary>Gets the brightest pixel's relative luminance in <c>[0, 1]</c>.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double MaximumRelativeLuminance
    {
        get
        {
            EnsureMeasured();
            return maximumLuminance;
        }
    }

    /// <summary>
    /// Gets the base-2 logarithm of the ratio between the brightest pixel and the darkest
    /// <em>non-black</em> one — the frame's realized dynamic range, in stops.
    /// </summary>
    /// <remarks>
    /// Absolute black is excluded because almost every frame contains some and it would make the
    /// ratio infinite, saying nothing. What is left is the span the frame actually uses, which for
    /// an 8-bit sRGB frame is bounded above by about 11.7 stops — level 1 decodes to 0.000304 of
    /// full white, and the base-2 logarithm of that ratio is 11.69. Reaching that bound is a sign
    /// the encoding, not the scene, is the limit. A frame with no lit pixel at all reports zero.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double DynamicRangeStops
    {
        get
        {
            EnsureMeasured();
            return maximumLuminance <= 0d ? 0d : Math.Log2(maximumLuminance / minimumPositiveLuminance);
        }
    }

    /// <summary>Measures a frame and returns a fresh instance holding the result.</summary>
    /// <param name="pixels">The frame to measure. Ownership stays with the caller.</param>
    /// <remarks>
    /// The convenience form, for a one-off measurement. Measuring a whole sequence should construct
    /// one <see cref="FrameStats"/> and call <see cref="Measure(PixelFrameLease)"/> per frame
    /// instead, which allocates nothing after the first.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pixels"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public static FrameStats Of(PixelFrameLease pixels)
    {
        var stats = new FrameStats();
        stats.Measure(pixels);
        return stats;
    }

    /// <summary>Measures a raw pixel buffer and returns a fresh instance holding the result.</summary>
    /// <param name="pixels">The pixel bytes, top row first.</param>
    /// <param name="width">The positive frame width in pixels.</param>
    /// <param name="height">The positive frame height in pixels.</param>
    /// <param name="stride">The byte distance between row starts, at least four bytes per pixel.</param>
    /// <param name="format">The byte and alpha layout of <paramref name="pixels"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is out of range.</exception>
    /// <exception cref="ArgumentException">The format is undefined or the buffer is too short.</exception>
    public static FrameStats Of(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride,
        PixelFormat format)
    {
        var stats = new FrameStats();
        stats.Measure(pixels, width, height, stride, format);
        return stats;
    }

    /// <summary>Measures a frame, replacing whatever this instance previously held.</summary>
    /// <param name="pixels">The frame to measure. Ownership stays with the caller.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pixels"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public void Measure(PixelFrameLease pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Measure(pixels.Pixels, pixels.Width, pixels.Height, pixels.Stride, pixels.Format);
    }

    /// <summary>Measures a raw pixel buffer, replacing whatever this instance previously held.</summary>
    /// <param name="pixels">The pixel bytes, top row first.</param>
    /// <param name="width">The positive frame width in pixels.</param>
    /// <param name="height">The positive frame height in pixels.</param>
    /// <param name="stride">The byte distance between row starts, at least four bytes per pixel.</param>
    /// <param name="format">The byte and alpha layout of <paramref name="pixels"/>.</param>
    /// <remarks>
    /// Bytes between the end of a row's pixels and the next row's start are stride padding and are
    /// not measured; they are not part of the image and counting them would move every percentile.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is out of range.</exception>
    /// <exception cref="ArgumentException">The format is undefined or the buffer is too short.</exception>
    public void Measure(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride,
        PixelFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                $"A {width}-pixel row needs at least {rowBytes} stride bytes.");
        }
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The pixel format is not defined.", nameof(format));
        }

        var required = checked((stride * (height - 1)) + rowBytes);
        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"A {width}×{height} frame at stride {stride} needs {required} bytes; " +
                $"{pixels.Length} were supplied.",
                nameof(pixels));
        }

        Reset();
        var (redOffset, blueOffset) = format == PixelFormat.Bgra8888Premul ? (2, 0) : (0, 2);
        var premultiplied = format != PixelFormat.Rgba8888;
        var table = LinearOfLevel;
        var buckets = histograms;

        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, rowBytes);
            for (var x = 0; x < rowBytes; x += 4)
            {
                int red = row[x + redOffset];
                int green = row[x + 1];
                int blue = row[x + blueOffset];
                int alpha = row[x + 3];

                buckets[((int)FrameChannel.Red * Levels) + red]++;
                buckets[((int)FrameChannel.Green * Levels) + green]++;
                buckets[((int)FrameChannel.Blue * Levels) + blue]++;
                buckets[((int)FrameChannel.Alpha * Levels) + alpha]++;

                var floor = red < green ? red : green;
                floor = floor < blue ? floor : blue;
                var ceiling = red > green ? red : green;
                ceiling = ceiling > blue ? ceiling : blue;
                buckets[((int)FrameChannel.ChannelFloor * Levels) + floor]++;
                buckets[((int)FrameChannel.ChannelCeiling * Levels) + ceiling]++;

                // Rounded rather than truncated: the weights sum to one, so a neutral grey must
                // land back on its own level, and truncation would pull every grey down by one.
                var encoded = (RedWeight * red) + (GreenWeight * green) + (BlueWeight * blue);
                buckets[((int)FrameChannel.Luma * Levels) + (int)(encoded + 0.5d)]++;

                var luminance = (RedWeight * table[red])
                    + (GreenWeight * table[green])
                    + (BlueWeight * table[blue]);
                luminanceSum += luminance;
                if (luminance < minimumLuminance)
                {
                    minimumLuminance = luminance;
                }
                if (luminance > 0d && luminance < minimumPositiveLuminance)
                {
                    minimumPositiveLuminance = luminance;
                }
                if (luminance > maximumLuminance)
                {
                    maximumLuminance = luminance;
                }

                if (premultiplied && (red > alpha || green > alpha || blue > alpha))
                {
                    invalidPixels++;
                }
            }
        }

        if (minimumLuminance > maximumLuminance)
        {
            minimumLuminance = maximumLuminance;
        }
        if (minimumPositiveLuminance > maximumLuminance)
        {
            minimumPositiveLuminance = maximumLuminance;
        }

        measuredWidth = width;
        measuredHeight = height;
        measuredFormat = format;
        HasMeasurement = true;
    }

    /// <summary>Gets one channel's full 256-bucket distribution, indexed by level.</summary>
    /// <param name="channel">The quantity whose histogram to view.</param>
    /// <returns>A read-only view of this instance's own storage; it changes when the instance is re-measured.</returns>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public ReadOnlySpan<int> Histogram(FrameChannel channel)
    {
        EnsureMeasured();
        return histograms.AsSpan(BucketOffset(channel), Levels);
    }

    /// <summary>Gets how many pixels sit at exactly one level of one channel.</summary>
    /// <param name="channel">The quantity to count.</param>
    /// <param name="level">The 8-bit level.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long CountAtLevel(FrameChannel channel, byte level)
    {
        EnsureMeasured();
        return histograms[BucketOffset(channel) + level];
    }

    /// <summary>Gets how many pixels reach or exceed a level.</summary>
    /// <param name="channel">The quantity to count.</param>
    /// <param name="level">The inclusive lower bound.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long CountAtOrAbove(FrameChannel channel, byte level)
    {
        EnsureMeasured();
        var offset = BucketOffset(channel);
        var total = 0L;
        for (var value = (int)level; value < Levels; value++)
        {
            total += histograms[offset + value];
        }

        return total;
    }

    /// <summary>Gets how many pixels are at or below a level.</summary>
    /// <param name="channel">The quantity to count.</param>
    /// <param name="level">The inclusive upper bound.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long CountAtOrBelow(FrameChannel channel, byte level)
    {
        EnsureMeasured();
        var offset = BucketOffset(channel);
        var total = 0L;
        for (var value = 0; value <= level; value++)
        {
            total += histograms[offset + value];
        }

        return total;
    }

    /// <summary>Gets the lowest level any pixel reaches in a channel.</summary>
    /// <param name="channel">The quantity to inspect.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public byte Minimum(FrameChannel channel)
    {
        EnsureMeasured();
        var offset = BucketOffset(channel);
        for (var value = 0; value < Levels; value++)
        {
            if (histograms[offset + value] > 0)
            {
                return (byte)value;
            }
        }

        return 0;
    }

    /// <summary>Gets the highest level any pixel reaches in a channel.</summary>
    /// <param name="channel">The quantity to inspect.</param>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public byte Maximum(FrameChannel channel)
    {
        EnsureMeasured();
        var offset = BucketOffset(channel);
        for (var value = Levels - 1; value >= 0; value--)
        {
            if (histograms[offset + value] > 0)
            {
                return (byte)value;
            }
        }

        return 0;
    }

    /// <summary>Gets a channel's arithmetic mean level, on the 0–255 scale.</summary>
    /// <param name="channel">The quantity to average.</param>
    /// <remarks>
    /// This averages code levels, so it is a mean of an encoded quantity and is not the frame's mean
    /// light output even for <see cref="FrameChannel.Luma"/>. Reach for
    /// <see cref="MeanRelativeLuminance"/> when the number has to be physical.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double Mean(FrameChannel channel)
    {
        EnsureMeasured();
        var offset = BucketOffset(channel);
        var weighted = 0L;
        for (var value = 0; value < Levels; value++)
        {
            weighted += (long)value * histograms[offset + value];
        }

        return (double)weighted / PixelCount;
    }

    /// <summary>Gets the level at a quantile of a channel's distribution.</summary>
    /// <param name="channel">The quantity to take the percentile of.</param>
    /// <param name="quantile">The quantile in <c>[0, 1]</c>; <c>0.9</c> is the 90th percentile.</param>
    /// <returns>The lowest level at or below which at least that fraction of pixels sits.</returns>
    /// <remarks>
    /// <para>
    /// <b>Nearest-rank, deliberately.</b> The result is the value of the pixel at rank
    /// <c>ceil(q · N)</c> in sorted order, clamped to <c>[1, N]</c>. It is therefore always a level
    /// some pixel actually has, which the interpolated definitions are not — and interpolating
    /// between two adjacent 8-bit codes invents a precision the frame does not contain.
    /// </para>
    /// <para>
    /// The rank is computed in <see cref="decimal"/> rather than <see cref="double"/>, because a
    /// binary product overshoots an exact integer for ordinary quantiles and the ceiling then asks
    /// for one rank too many.
    /// </para>
    /// <para>
    /// The two ends are the ones implementations get wrong, so they are pinned here:
    /// <c>Percentile(c, 1.0)</c> is exactly <see cref="Maximum(FrameChannel)"/> — a rank of <c>N</c>
    /// selects the last pixel, and an implementation indexing <c>(int)(q · N)</c> off the end or
    /// comparing with a strict <c>&gt;</c> loses the top bucket entirely — and
    /// <c>Percentile(c, 0.0)</c> is exactly <see cref="Minimum(FrameChannel)"/>, because rank zero
    /// is not a pixel and clamps up to the first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="channel"/> is not a defined channel.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quantile"/> is not in <c>[0, 1]</c>.</exception>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public byte Percentile(FrameChannel channel, double quantile)
    {
        EnsureMeasured();
        if (!(quantile >= 0d && quantile <= 1d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantile),
                quantile,
                "A quantile must lie in [0, 1]; 0.9 is the 90th percentile.");
        }

        var pixels = PixelCount;

        // Decimal, not double. The rank is a count, and binary floating point cannot hold most
        // quantiles exactly: 0.07 * 100 is 7.000000000000001, whose ceiling is 8, so the seventh
        // percentile of a hundred-pixel frame would quietly answer with the eighth pixel. Decimal
        // represents the quantiles anyone actually types, so the product is exact for them and the
        // ceiling means what it says.
        var rank = (long)Math.Ceiling((decimal)quantile * pixels);
        rank = Math.Clamp(rank, 1L, pixels);

        var offset = BucketOffset(channel);
        var cumulative = 0L;
        for (var value = 0; value < Levels; value++)
        {
            cumulative += histograms[offset + value];
            if (cumulative >= rank)
            {
                return (byte)value;
            }
        }

        return (byte)(Levels - 1);
    }

    /// <summary>Gets how many pixels have every colour channel at or above a level.</summary>
    /// <param name="level">The clipping threshold; 255 counts only pixels that are exactly white.</param>
    /// <remarks>
    /// A lower threshold is often the honest one. Output dither, a codec round trip, or a
    /// near-white ramp top all put pixels at 253–254 that are white for every practical purpose, and
    /// a study of this frame's clipping counted them by measuring at 254.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long ClippedWhitePixels(byte level = 255) => CountAtOrAbove(FrameChannel.ChannelFloor, level);

    /// <summary>Gets <see cref="ClippedWhitePixels(byte)"/> as a fraction of the frame in <c>[0, 1]</c>.</summary>
    /// <param name="level">The clipping threshold.</param>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double ClippedWhiteFraction(byte level = 255) => (double)ClippedWhitePixels(level) / PixelCount;

    /// <summary>Gets how many pixels have every colour channel at or below a level.</summary>
    /// <param name="level">The floor threshold; 0 counts only pixels that are exactly black.</param>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public long CrushedBlackPixels(byte level = 0) => CountAtOrBelow(FrameChannel.ChannelCeiling, level);

    /// <summary>Gets <see cref="CrushedBlackPixels(byte)"/> as a fraction of the frame in <c>[0, 1]</c>.</summary>
    /// <param name="level">The floor threshold.</param>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public double CrushedBlackFraction(byte level = 0) => (double)CrushedBlackPixels(level) / PixelCount;

    /// <summary>Renders the measurement as a multi-line table, for a log or a render banner.</summary>
    /// <remarks>
    /// Formatted with the invariant culture so a number pasted into a document reads the same
    /// everywhere. This is the shape the grading loop wanted: one call whose output can be diffed
    /// against the previous take.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been measured yet.</exception>
    public string Describe()
    {
        EnsureMeasured();
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.Append(culture, $"{measuredWidth}×{measuredHeight} {measuredFormat}, {PixelCount} px")
            .AppendLine()
            .AppendLine("channel          min     p50     p90     p99     max      mean");

        foreach (var channel in DescribedChannels)
        {
            text.Append(culture, $"{channel,-12}{Minimum(channel),8}{Percentile(channel, 0.5d),8}")
                .Append(culture, $"{Percentile(channel, 0.9d),8}{Percentile(channel, 0.99d),8}")
                .Append(culture, $"{Maximum(channel),8}{Mean(channel),10:F2}")
                .AppendLine();
        }

        text.Append(culture, $"clipped white (all channels ≥ 255): {ClippedWhitePixels()} px, ")
            .Append(culture, $"{ClippedWhiteFraction() * 100d:F4}%")
            .AppendLine()
            .Append(culture, $"crushed black (all channels ≤ 0):   {CrushedBlackPixels()} px, ")
            .Append(culture, $"{CrushedBlackFraction() * 100d:F4}%")
            .AppendLine()
            .Append(culture, $"relative luminance: mean {MeanRelativeLuminance:F6}, ")
            .Append(culture, $"max {MaximumRelativeLuminance:F6}, ")
            .Append(culture, $"range {DynamicRangeStops:F2} stops")
            .AppendLine()
            .Append(culture, $"all pixels opaque: {(AllPixelsOpaque ? "yes" : "no")}; ")
            .Append(culture, $"invalid pixels: {InvalidPixels}")
            .AppendLine();

        return text.ToString();
    }

    private static double[] BuildLinearTable()
    {
        var table = new double[Levels];
        for (var level = 0; level < Levels; level++)
        {
            table[level] = Color.SrgbToLinear(level / 255f);
        }

        return table;
    }

    private static int BucketOffset(FrameChannel channel)
    {
        if ((uint)channel > (uint)FrameChannel.ChannelCeiling)
        {
            throw new ArgumentException($"'{channel}' is not a defined frame channel.", nameof(channel));
        }

        return (int)channel * Levels;
    }

    private void Reset()
    {
        Array.Clear(histograms);
        luminanceSum = 0d;
        minimumLuminance = double.MaxValue;
        minimumPositiveLuminance = double.MaxValue;
        maximumLuminance = 0d;
        invalidPixels = 0L;
        HasMeasurement = false;
    }

    private void EnsureMeasured()
    {
        if (!HasMeasurement)
        {
            throw new InvalidOperationException(
                $"No frame has been measured into these {nameof(FrameStats)} yet. Call " +
                $"{nameof(Measure)} first, or use {nameof(FrameStats)}.{nameof(Of)}.");
        }
    }
}
