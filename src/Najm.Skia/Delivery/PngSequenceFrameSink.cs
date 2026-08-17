using System.Globalization;
using Najm.Core;

namespace Najm.Skia;

/// <summary>Writes each frame to its own numbered PNG file.</summary>
/// <remarks>
/// <para>
/// <strong>This is the non-default delivery path, and it is enormous.</strong> A PNG sequence keeps
/// every frame on disk uncompressed-by-video-standards: roughly 8 MB per 1080p frame and 30 MB per
/// 4K frame, so twelve seconds of 4K at 60 fps is on the order of thirty gigabytes before an encoder
/// has seen a single pixel. Check the free space on the volume before starting a long run, and
/// expect the write, not the render, to be the bottleneck.
/// </para>
/// <para>
/// <strong>Prefer <see cref="FrameSink.FfmpegPipe(string, FfmpegPipeOptions?)"/></strong> for
/// anything that is going to become a video. It pipes the same raw frames straight into an encoder
/// and never touches the disk between the renderer and the finished file. Reach for a sequence only
/// when the individual frames are the deliverable — a frame-by-frame diff, a compositing hand-off,
/// or an encoder Najm does not drive.
/// </para>
/// <para>
/// Files are named <c>{name}_00000.png</c>, zero-padded to five digits and numbered by output frame
/// index, so <c>ffmpeg -i name_%05d.png</c> reads them back in order. Indices past 99999 simply grow
/// wider, which breaks that pattern; a run that long belongs in a pipe anyway.
/// </para>
/// </remarks>
public sealed class PngSequenceFrameSink : IFrameSink
{
    private readonly string directory;
    private readonly string name;
    private int width;
    private int height;
    private long lastFrame = -1L;
    private long writtenFrames;
    private bool begun;
    private bool ended;

    internal PngSequenceFrameSink(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.AsSpan().ContainsAny(Path.GetInvalidFileNameChars()))
        {
            throw new ArgumentException(
                $"The frame name '{name}' contains characters that cannot appear in a file name.",
                nameof(name));
        }

        this.directory = Path.GetFullPath(directory);
        this.name = name;
    }

    /// <summary>Gets the absolute directory the numbered frames are written to.</summary>
    public string Directory => directory;

    /// <summary>Gets the file-name stem each frame's index is appended to.</summary>
    public string Name => name;

    /// <summary>Gets how many PNG files this sink has written.</summary>
    public long WrittenFrames => writtenFrames;

    /// <inheritdoc />
    /// <remarks>Creates the output directory and fixes the frame size every file must match.</remarks>
    /// <exception cref="InvalidOperationException">The sink has already begun a stream.</exception>
    public void Begin(in FrameStreamInfo info)
    {
        if (begun)
        {
            throw new InvalidOperationException("This PNG sequence sink has already begun a stream.");
        }
        if (!info.IsValid)
        {
            throw new ArgumentException("A frame stream description is required.", nameof(info));
        }

        System.IO.Directory.CreateDirectory(directory);
        width = info.Width;
        height = info.Height;
        begun = true;
    }

    /// <inheritdoc />
    /// <remarks>Encodes the frame synchronously and disposes the lease.</remarks>
    /// <exception cref="InvalidOperationException">
    /// The stream has not begun or has ended, the frame index does not advance, or the frame's size
    /// disagrees with the stream.
    /// </exception>
    public void Submit(long frame, PixelFrameLease pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        // Ownership transferred on entry: this sink disposes the lease on every path.
        using (pixels)
        {
            if (!begun)
            {
                throw new InvalidOperationException("This PNG sequence sink has not begun a stream.");
            }
            if (ended)
            {
                throw new InvalidOperationException("This PNG sequence sink has already ended its stream.");
            }
            if (frame <= lastFrame)
            {
                throw new InvalidOperationException(
                    $"Frame indices must increase; received {frame} after {lastFrame}.");
            }
            if (pixels.Width != width || pixels.Height != height)
            {
                throw new InvalidOperationException(
                    $"Frame {frame} is {pixels.Width}×{pixels.Height} but the stream declared " +
                    $"{width}×{height}.");
            }

            SkiaPngWriter.Write(pixels, PathForFrame(frame));
            lastFrame = frame;
            writtenFrames++;
        }
    }

    /// <inheritdoc />
    /// <remarks>Nothing is buffered, so ending a sequence only closes it to further frames.</remarks>
    /// <exception cref="InvalidOperationException">The stream never began or has already ended.</exception>
    public void End()
    {
        if (!begun)
        {
            throw new InvalidOperationException("This PNG sequence sink has not begun a stream.");
        }
        if (ended)
        {
            throw new InvalidOperationException("This PNG sequence sink has already ended its stream.");
        }

        ended = true;
    }

    /// <summary>Returns the absolute file path this sink writes one output frame index to.</summary>
    /// <param name="frame">The zero-based output frame index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is negative.</exception>
    public string PathForFrame(long frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        return Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"{name}_{frame:D5}.png"));
    }
}
