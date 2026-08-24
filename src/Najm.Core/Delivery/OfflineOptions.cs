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
    private readonly double framesPerSecond = 60d;
    private readonly double? duration;
    private readonly long? frames;
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
    /// Gets the run length in simulated seconds, or null when <see cref="Frames"/> supplies the
    /// length instead.
    /// </summary>
    /// <remarks>
    /// A duration is converted with <see cref="FixedStepTiming.TicksForStill(double, double)"/>, so
    /// 0.5 seconds at 60 fps is exactly 30 frames and any positive fraction of a frame rounds up to
    /// a whole frame.
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
    /// <strong>Precedence.</strong> When both this and <see cref="Duration"/> are set, this wins and
    /// the duration is ignored. The reference leaves the combination unspecified; Najm resolves it
    /// toward the more precise of the two, because a frame count states the answer a duration only
    /// implies, and silently rejecting the pair would break the common case of overriding a
    /// configured duration with an exact count for a test.
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

    /// <summary>Resolves how many frames this configuration renders.</summary>
    /// <remarks>
    /// <see cref="Frames"/> wins over <see cref="Duration"/>; a duration alone becomes
    /// <c>ceil(duration × fps)</c> frames.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Neither a frame count nor a duration is set.</exception>
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
            "An offline render needs a length: set OfflineOptions.Frames or OfflineOptions.Duration.");
    }
}
