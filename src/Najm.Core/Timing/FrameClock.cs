namespace Najm.Core;

/// <summary>
/// Advances one host-owned clock according to a fixed or live
/// <see cref="ClockPolicy"/>.
/// </summary>
/// <remarks>
/// This type never reads wall time. A live host measures time externally and
/// supplies it to <see cref="Advance(double)"/>. The object allocates once when
/// constructed; successful steady-state advancement allocates no managed memory.
/// </remarks>
public sealed class FrameClock
{
    private readonly ClockPolicy policy;
    private long nextFrame;
    private double liveElapsed;
    private bool frameSpaceExhausted;

    /// <summary>Creates a fresh clock whose next frame is zero.</summary>
    public FrameClock(ClockPolicy policy)
    {
        if (!policy.IsValid)
        {
            throw new ArgumentException(
                "A frame clock requires a policy created by ClockPolicy.Fixed or ClockPolicy.Live.",
                nameof(policy));
        }

        this.policy = policy;
    }

    /// <summary>Gets this clock's immutable policy.</summary>
    public ClockPolicy Policy => policy;

    /// <summary>Advances a fixed-step clock by exactly one derived frame.</summary>
    /// <exception cref="InvalidOperationException">This is a live clock.</exception>
    /// <exception cref="OverflowException">No further frame index or elapsed time is representable.</exception>
    public TimeInfo Advance()
    {
        if (!policy.IsFixedStep)
        {
            throw new InvalidOperationException(
                "A live clock advances only when the host supplies a wall-time delta.");
        }

        EnsureFrameAvailable();
        var frame = nextFrame;
        var time = FixedStepTiming.Tick(frame, policy.FramesPerSecond);
        CommitFrame(frame);
        return time;
    }

    /// <summary>
    /// Advances a live clock using caller-measured wall delta. The finite,
    /// non-negative value is clamped to <see cref="ClockPolicy.MaxDt"/> before it
    /// is accumulated; returned elapsed time has post-advance meaning.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is a fixed-step clock.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="wallDt"/> is invalid.</exception>
    /// <exception cref="OverflowException">No further frame index or elapsed time is representable.</exception>
    public TimeInfo Advance(double wallDt)
    {
        if (policy.IsFixedStep)
        {
            throw new InvalidOperationException(
                "A fixed-step clock derives its delta and does not accept wall time.");
        }
        if (!double.IsFinite(wallDt) || wallDt < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wallDt),
                wallDt,
                "A live wall-time delta must be finite and non-negative.");
        }

        EnsureFrameAvailable();
        var dt = Math.Min(wallDt, policy.MaxDt);
        var elapsed = liveElapsed + dt;
        if (!double.IsFinite(elapsed))
        {
            throw new OverflowException("Live elapsed time exceeds the finite double range.");
        }

        var frame = nextFrame;
        var time = new TimeInfo(elapsed, dt, frame, isFixedStep: false);
        liveElapsed = elapsed;
        CommitFrame(frame);
        return time;
    }

    private void EnsureFrameAvailable()
    {
        if (frameSpaceExhausted)
        {
            throw new OverflowException("The clock exhausted the Int64 frame-index range.");
        }
    }

    private void CommitFrame(long emittedFrame)
    {
        if (emittedFrame == long.MaxValue)
        {
            frameSpaceExhausted = true;
            return;
        }

        nextFrame = emittedFrame + 1L;
    }
}

