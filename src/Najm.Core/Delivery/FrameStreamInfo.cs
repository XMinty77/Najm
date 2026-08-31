namespace Najm.Core;

/// <summary>Describes the frame stream a sink is about to receive.</summary>
/// <remarks>
/// This is handed to <see cref="IFrameSink.Begin(in FrameStreamInfo)"/> once, before any frame, and
/// is constant for the whole stream: a sink may size buffers, spawn an encoder, or write a header
/// from it and never revalidate. The zero-initialized value is invalid.
/// </remarks>
public readonly record struct FrameStreamInfo
{
    /// <summary>Creates a validated description of one frame stream.</summary>
    /// <param name="width">The positive frame width in pixels.</param>
    /// <param name="height">The positive frame height in pixels.</param>
    /// <param name="framesPerSecond">The finite, positive presentation rate.</param>
    /// <param name="format">The byte and alpha layout every submitted lease carries.</param>
    /// <param name="frameCount">
    /// The total number of frames the producer intends to submit, or null when the length is not
    /// known in advance — a live capture that runs until the user stops it, or an offline run whose
    /// length is the scene's own choreography (<see cref="OfflineOptions.RunsUntilIdle"/>). An
    /// offline run with a stated length always knows it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, <paramref name="framesPerSecond"/> is not finite and positive,
    /// or <paramref name="frameCount"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="format"/> is not a defined format.</exception>
    public FrameStreamInfo(
        int width,
        int height,
        double framesPerSecond,
        PixelFormat format,
        long? frameCount = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesPerSecond),
                framesPerSecond,
                "A frame stream's rate must be finite and positive.");
        }
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The stream pixel format is not defined.", nameof(format));
        }
        if (frameCount is < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                frameCount,
                "A known frame count cannot be negative.");
        }

        Width = width;
        Height = height;
        FramesPerSecond = framesPerSecond;
        Format = format;
        FrameCount = frameCount;
    }

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the presentation rate in frames per simulated second.</summary>
    public double FramesPerSecond { get; }

    /// <summary>Gets the byte and alpha layout every submitted lease carries.</summary>
    public PixelFormat Format { get; }

    /// <summary>Gets the total frame count when the producer knows it, otherwise null.</summary>
    public long? FrameCount { get; }

    /// <summary>Gets whether this value was constructed rather than zero-initialized.</summary>
    public bool IsValid => Width > 0 && Height > 0 && FramesPerSecond > 0d;

    /// <summary>Gets the frame dimensions as a <see cref="PixelSize"/>.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid zero-initialized value.</exception>
    public PixelSize Size
    {
        get
        {
            if (!IsValid)
            {
                throw new InvalidOperationException(
                    "The zero-initialized FrameStreamInfo is invalid and does not describe a stream.");
            }

            return new PixelSize(Width, Height);
        }
    }

    /// <summary>Gets the meaningful bytes in one tightly packed row, four per pixel.</summary>
    public int RowBytes => checked(Width * 4);
}
