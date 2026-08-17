namespace Najm.Skia;

/// <summary>Configures the ffmpeg process <see cref="FfmpegFrameSink"/> spawns.</summary>
/// <remarks>
/// The defaults encode H.264 at <c>-preset slow -crf 16 -pix_fmt yuv420p</c>, which is the intended
/// delivery path: raw frames go straight down a pipe and only the encoded file ever reaches the
/// disk.
/// </remarks>
public sealed record FfmpegPipeOptions
{
    private readonly string executable = "ffmpeg";
    private readonly FfmpegVideoCodec codec = FfmpegVideoCodec.H264;
    private readonly string? outputPixelFormat;
    private readonly string preset = "slow";
    private readonly int constantRateFactor = 16;
    private readonly int proResProfile = 3;
    private readonly string logLevel = "error";
    private readonly TimeSpan shutdownTimeout = TimeSpan.FromMinutes(5);
    private readonly IReadOnlyList<string> extraArguments = [];

    /// <summary>Gets the ffmpeg executable, resolved on <c>PATH</c> unless it is a full path.</summary>
    /// <remarks>The default is <c>ffmpeg</c>. ffmpeg is never linked, only spawned.</remarks>
    /// <exception cref="ArgumentException">The value is null or whitespace.</exception>
    public string Executable
    {
        get => executable;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            executable = value;
        }
    }

    /// <summary>Gets the video encoder. The default is <see cref="FfmpegVideoCodec.H264"/>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined codec.</exception>
    public FfmpegVideoCodec Codec
    {
        get => codec;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The requested ffmpeg video codec is not defined.", nameof(value));
            }

            codec = value;
        }
    }

    /// <summary>Gets the x264 preset. The default is <c>slow</c>. Ignored by ProRes.</summary>
    /// <exception cref="ArgumentException">The value is null or whitespace.</exception>
    public string Preset
    {
        get => preset;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            preset = value;
        }
    }

    /// <summary>Gets the x264 constant rate factor. The default is 16. Ignored by ProRes.</summary>
    /// <remarks>Lower is better and larger; 0 is lossless and 51 is the worst legal value.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 51.</exception>
    public int ConstantRateFactor
    {
        get => constantRateFactor;
        init
        {
            if (value is < 0 or > 51)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An x264 constant rate factor must lie in [0, 51].");
            }

            constantRateFactor = value;
        }
    }

    /// <summary>Gets the ProRes profile. The default is 3, which is ProRes 422 HQ.</summary>
    /// <remarks>0 is Proxy, 1 LT, 2 standard 422, 3 HQ, 4 4444, and 5 4444 XQ.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 5.</exception>
    public int ProResProfile
    {
        get => proResProfile;
        init
        {
            if (value is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A prores_ks profile must lie in [0, 5].");
            }

            proResProfile = value;
        }
    }

    /// <summary>
    /// Gets the encoded pixel format, or null to take the codec's default — <c>yuv420p</c> for
    /// H.264 and <c>yuv422p10le</c> for ProRes.
    /// </summary>
    /// <remarks>
    /// This is the <em>output</em> format. The pipe's input is always the raw RGBA the renderer
    /// produced.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is whitespace.</exception>
    public string? OutputPixelFormat
    {
        get => outputPixelFormat;
        init
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
            }

            outputPixelFormat = value;
        }
    }

    /// <summary>Gets whether an existing output file is overwritten. The default is true.</summary>
    /// <remarks>False passes <c>-n</c>, so ffmpeg refuses rather than clobbering, and the sink fails
    /// loudly at <see cref="IFrameSink.End"/>.</remarks>
    public bool Overwrite { get; init; } = true;

    /// <summary>Gets ffmpeg's log level. The default is <c>error</c>.</summary>
    /// <remarks>
    /// Whatever ffmpeg writes to standard error is drained and its tail is quoted back in any
    /// failure message, so a quiet level still produces a diagnosable error. Raise it to
    /// <c>verbose</c> when an encode needs investigating.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is null or whitespace.</exception>
    public string LogLevel
    {
        get => logLevel;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            logLevel = value;
        }
    }

    /// <summary>
    /// Gets how long the sink waits for ffmpeg to exit after its input closes. The default is five
    /// minutes.
    /// </summary>
    /// <remarks>
    /// The wait covers the encoder draining its lookahead queue, which for a long clip at
    /// <c>-preset slow</c> is real work. Exceeding it kills the process and fails loudly.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan ShutdownTimeout
    {
        get => shutdownTimeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The ffmpeg shutdown timeout must be positive.");
            }

            shutdownTimeout = value;
        }
    }

    /// <summary>Gets extra ffmpeg arguments inserted immediately before the output path.</summary>
    /// <remarks>
    /// The escape hatch for anything this record does not model — a filter chain, a container flag,
    /// a different encoder entirely. Arguments are passed through unquoted and unparsed.
    /// </remarks>
    /// <exception cref="ArgumentException">An entry is null or whitespace.</exception>
    public IReadOnlyList<string> ExtraArguments
    {
        get => extraArguments;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var argument in value)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(argument, nameof(value));
            }

            extraArguments = [.. value];
        }
    }

    /// <summary>Resolves the encoded pixel format this configuration produces.</summary>
    internal string ResolveOutputPixelFormat() =>
        OutputPixelFormat ?? Codec switch
        {
            FfmpegVideoCodec.ProRes => "yuv422p10le",
            _ => "yuv420p",
        };
}
