namespace Najm.Core;

/// <summary>Pure helpers for the normative fixed-step frame convention.</summary>
public static class FixedStepTiming
{
    private const double LongUpperExclusive = 9_223_372_036_854_775_808d;

    /// <summary>
    /// Derives fixed tick <paramref name="frame"/> directly, without cumulative
    /// time addition: Dt = 1/fps and Elapsed = (frame+1)/fps.
    /// </summary>
    public static TimeInfo Tick(long frame, double framesPerSecond)
    {
        if (frame < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                frame,
                "Frame indices must be non-negative.");
        }

        var policy = ClockPolicy.Fixed(framesPerSecond);
        var dt = 1d / policy.FramesPerSecond;
        var elapsed = ((double)frame + 1d) / policy.FramesPerSecond;

        if (!double.IsFinite(elapsed))
        {
            throw new OverflowException("The fixed-step elapsed time exceeds the finite double range.");
        }

        return new TimeInfo(elapsed, dt, frame, isFixedStep: true);
    }

    /// <summary>
    /// Returns the number of fixed ticks required before rendering a still at
    /// time <paramref name="at"/>: ceil(at × fps). Exactly zero seconds requires
    /// zero ticks; every positive representable time requires at least one.
    /// </summary>
    /// <remarks>
    /// Division-derived frame boundaries can multiply back to an integer plus
    /// or minus a rounding ULP. When the product is within two ULPs of an
    /// integer, this method compares <paramref name="at"/> with that integer's
    /// canonical <c>tick/fps</c> boundary. Equality and values below the boundary
    /// retain that tick; any representable value above it advances. This repairs
    /// exact-boundary round trips without applying a broad time epsilon.
    /// </remarks>
    public static long TicksForStill(double at, double framesPerSecond)
    {
        if (!double.IsFinite(at) || at < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(at),
                at,
                "Still time must be finite and non-negative.");
        }

        var policy = ClockPolicy.Fixed(framesPerSecond);
        if (at == 0d)
        {
            return 0L;
        }

        var product = at * policy.FramesPerSecond;
        if (!double.IsFinite(product))
        {
            throw new OverflowException("The still time requires more ticks than can be represented.");
        }
        if (product >= LongUpperExclusive)
        {
            throw new OverflowException("The still time requires more than Int64.MaxValue ticks.");
        }

        var nearestInteger = Math.Round(product);
        if (IsWithinTwoUlps(product, nearestInteger))
        {
            var candidate = checked((long)nearestInteger);
            var canonicalBoundary = (double)candidate / policy.FramesPerSecond;

            if (at <= canonicalBoundary)
            {
                return candidate;
            }
            if (candidate == long.MaxValue)
            {
                throw new OverflowException("The still time requires more than Int64.MaxValue ticks.");
            }

            return candidate + 1L;
        }

        var ticks = Math.Ceiling(product);
        if (ticks == 0d)
        {
            return 1L;
        }
        if (ticks >= LongUpperExclusive)
        {
            throw new OverflowException("The still time requires more than Int64.MaxValue ticks.");
        }

        return checked((long)ticks);
    }

    private static bool IsWithinTwoUlps(double value, double target)
    {
        var lower = Math.BitDecrement(Math.BitDecrement(target));
        var upper = Math.BitIncrement(Math.BitIncrement(target));
        return value >= lower && value <= upper;
    }
}
