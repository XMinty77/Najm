using Najm.Core.Text;

namespace Najm.Core;

/// <summary>Configures one deterministic offline render.</summary>
/// <remarks>
/// Output pixel size is a driver parameter and never a scene concern: the scene is authored once
/// against its <see cref="Scene.VirtualResolution"/>, and a draft, a 1080p, and a 4K encode of it
/// differ only in <see cref="Scale"/> or <see cref="OutputSize"/>.
/// </remarks>
public sealed class OfflineOptions
{
    /// <summary>The open-ended run's default ceiling: one hour of simulated time.</summary>
    private const double DefaultLimitSeconds = 3600d;

    private readonly double framesPerSecond = 60d;
    private readonly double? duration;
    private readonly long? frames;
    private readonly long? maxFrames;
    private readonly float scale = 1f;
    private readonly int sampleCount = 1;
    private readonly PixelFormat format = PixelFormat.Rgba8888;
    private readonly ColorSpace colorSpace = ColorSpace.Srgb;

    /// <summary>Gets the sink every rendered frame is submitted to.</summary>
    public required IFrameSink Sink { get; init; }

    /// <summary>
    /// Gets the typesetter the rendered scene measures and draws text through, or null for the
    /// fail-loud <see cref="NullTypesetter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An offline run has no host, so nothing else would ever put a real typesetter into the scene's
    /// environment: without this option the loop builds an environment around the surface provider
    /// alone and every text node in the scene fails at attach. That is a correct failure and a
    /// useless one — a deterministic export of a figure with a caption on it is the ordinary case,
    /// not an exotic one.
    /// </para>
    /// <para>
    /// The default stays <see cref="NullTypesetter"/>, so a run that draws no text pulls in no text
    /// assembly and an omission still reports itself by name rather than by a blank frame.
    /// <c>Najm.Text.Typesetter</c> is the one implementation.
    /// </para>
    /// </remarks>
    public ITypesetter? Typesetter { get; init; }

    /// <summary>Gets the fixed simulation and presentation rate. The default is 60.</summary>
    /// <remarks>
    /// Under this rate, tick <c>k</c> carries <c>Dt = 1/fps</c> and <c>Elapsed = (k+1)/fps</c>, and
    /// output frame <c>k</c> is the render performed after tick <c>k</c>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public double Fps
    {
        get => framesPerSecond;
        init
        {
            _ = ClockPolicy.Fixed(value);
            framesPerSecond = value;
        }
    }

    /// <summary>
    /// Gets the run length in simulated seconds, or null to let <see cref="Frames"/> or the scene
    /// itself supply the length.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A duration is converted with <see cref="FixedStepTiming.TicksForStill(double, double)"/>, so
    /// 0.5 seconds at 60 fps is exactly 30 frames and any positive fraction of a frame rounds up to
    /// a whole frame.
    /// </para>
    /// <para>
    /// <strong>Null here and null in <see cref="Frames"/> means "until the scene's scheduled work
    /// finishes"</strong> — see <see cref="RunsUntilIdle"/>. The run then ticks until
    /// <see cref="Scene.HasScheduledWork"/> is false after a tick, and that tick's frame is the last
    /// one submitted. This exists because the alternative is a scene publishing its own length by
    /// hand, summing its beat constants, and being wrong: waits add whole frames that no constant
    /// can see — a spin on a condition, a rejoin from a helper routine — so the sum comes out short
    /// and the clip is cut before the choreography ends, silently. The scheduler knows when the
    /// routines are done and nothing else does.
    /// </para>
    /// <para>
    /// The cost of that mode is that its length is discovered rather than declared: the sink is
    /// begun with a null <see cref="FrameStreamInfo.FrameCount"/>, so a sink that needs the total up
    /// front cannot be used with it, and a routine that never finishes would run forever if
    /// <see cref="MaxFrames"/> did not stop it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and non-negative.</exception>
    public double? Duration
    {
        get => duration;
        init
        {
            if (value is { } seconds && (!double.IsFinite(seconds) || seconds < 0d))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    seconds,
                    "An offline duration must be finite and non-negative.");
            }

            duration = value;
        }
    }

    /// <summary>Gets the explicit frame count, or null to derive it from <see cref="Duration"/>.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Precedence.</strong> When both this and <see cref="Duration"/> are set, this wins and
    /// the duration is ignored. The reference leaves the combination unspecified; Najm resolves it
    /// toward the more precise of the two, because a frame count states the answer a duration only
    /// implies, and silently rejecting the pair would break the common case of overriding a
    /// configured duration with an exact count for a test.
    /// </para>
    /// <para>
    /// Null in both is not a missing length but a third one: see <see cref="RunsUntilIdle"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long? Frames
    {
        get => frames;
        init
        {
            if (value is < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An offline frame count cannot be negative.");
            }

            frames = value;
        }
    }

    /// <summary>
    /// Gets the virtual-to-output pixel scale. The default is one, meaning output pixels equal
    /// virtual units.
    /// </summary>
    /// <remarks>Ignored when <see cref="OutputSize"/> is set.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public float Scale
    {
        get => scale;
        init
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An offline render scale must be finite and positive.");
            }

            scale = value;
        }
    }

    /// <summary>Gets an explicit output size, or null to derive one from <see cref="Scale"/>.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Precedence.</strong> An explicit size wins over <see cref="Scale"/>. A size whose
    /// aspect differs from the scene's virtual resolution is letterboxed by the ordinary render-scale
    /// rule rather than stretched.
    /// </para>
    /// <para>
    /// <strong>Letterboxed means centred.</strong> The scene is fitted at the largest uniform scale
    /// that puts the whole virtual frame inside this size, and the fitted content rect is then
    /// centred in it — see <see cref="FramePlacement"/>, which states the offset rule and where the
    /// odd leftover pixel goes. The bars are left transparent: §5.1 makes bar color a host concern
    /// (<c>HostOptions.BarColor</c>) and an offline run has no host, so a color chosen here would be
    /// baked into files whose real background is decided later.
    /// </para>
    /// </remarks>
    public PixelSize? OutputSize { get; init; }

    /// <summary>Gets the pixel layout submitted to the sink. The default is <see cref="PixelFormat.Rgba8888"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined format.</exception>
    public PixelFormat Format
    {
        get => format;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The requested pixel format is not defined.", nameof(value));
            }

            format = value;
        }
    }

    /// <summary>Gets the requested surface sample count. The default is one.</summary>
    /// <remarks>CPU-raster providers normalize every sample count to one, since raster Skia is
    /// analytically antialiased.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int SampleCount
    {
        get => sampleCount;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            sampleCount = value;
        }
    }

    /// <summary>Gets the output surface's color-space tag. The default is <see cref="Najm.Core.ColorSpace.Srgb"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined color space.</exception>
    public ColorSpace ColorSpace
    {
        get => colorSpace;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The color-space tag is not defined.", nameof(value));
            }

            colorSpace = value;
        }
    }

    /// <summary>
    /// Gets whether this configuration runs until the scene's scheduled work finishes, rather than
    /// for a length stated in advance.
    /// </summary>
    /// <remarks>
    /// True when neither <see cref="Frames"/> nor <see cref="Duration"/> is set. The run ends after
    /// the first tick that leaves <see cref="Scene.HasScheduledWork"/> false, so a scene that
    /// schedules nothing is one frame long, and a scene holding a routine that never completes runs
    /// until <see cref="MaxFrames"/> stops it — with an exception, because ending an open-ended run
    /// early by quietly returning would be the truncation this mode exists to prevent.
    /// </remarks>
    public bool RunsUntilIdle => frames is null && duration is null;

    /// <summary>
    /// Gets the frame ceiling an open-ended run is stopped at, or null for the default of one hour
    /// of simulated time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="RunsUntilIdle"/> runs consult this; a run with a stated length already has
    /// its bound. Reaching it throws rather than returning the frames rendered so far: a run that
    /// stopped short and reported success would be indistinguishable from one that finished, which
    /// is the failure this whole mode is aimed at.
    /// </para>
    /// <para>
    /// The default is deliberately far beyond any plausible clip and far short of filling a disk.
    /// Set it explicitly for a genuinely longer run, or to <see cref="long.MaxValue"/> to accept an
    /// unbounded one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long? MaxFrames
    {
        get => maxFrames;
        init
        {
            if (value is { } limit)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
            }

            maxFrames = value;
        }
    }

    /// <summary>Resolves how many frames this configuration renders.</summary>
    /// <remarks>
    /// <see cref="Frames"/> wins over <see cref="Duration"/>; a duration alone becomes
    /// <c>ceil(duration × fps)</c> frames. An open-ended configuration has no answer to give here —
    /// its length is discovered by running it — so it throws rather than inventing one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The run is open-ended: <see cref="RunsUntilIdle"/> is true.
    /// </exception>
    /// <exception cref="OverflowException">The duration needs more frames than are representable.</exception>
    public long ResolveFrameCount()
    {
        if (frames is { } explicitCount)
        {
            return explicitCount;
        }
        if (duration is { } seconds)
        {
            return FixedStepTiming.TicksForStill(seconds, framesPerSecond);
        }

        throw new InvalidOperationException(
            "This offline configuration has no frame count: with neither Frames nor Duration set it "
            + "runs until the scene's scheduled work finishes, and its length is known only after "
            + "the run. Test OfflineOptions.RunsUntilIdle before asking.");
    }

    /// <summary>Resolves the frame ceiling an open-ended run is stopped at.</summary>
    /// <remarks>
    /// <see cref="MaxFrames"/> when set, otherwise one hour of simulated time at <see cref="Fps"/>.
    /// </remarks>
    /// <exception cref="OverflowException">The default bound needs more frames than are representable.</exception>
    public long ResolveFrameLimit() =>
        maxFrames ?? FixedStepTiming.TicksForStill(DefaultLimitSeconds, framesPerSecond);
}
