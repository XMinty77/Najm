using Najm.Core;

namespace Najm.Skia;

/// <summary>Creates the frame sinks Najm's media backend ships.</summary>
/// <remarks>
/// Three destinations, and they are not equals.
/// <see cref="FfmpegPipe(string, FfmpegPipeOptions?)"/> is the default for a sequence: raw frames go
/// down a pipe into an encoder and only the finished video reaches the disk.
/// <see cref="PngSequence(string, string)"/> writes one file per frame and is for the cases where
/// the frames themselves are the deliverable — read its remarks about size before pointing a long
/// render at it. <see cref="PngFile(string)"/> is the odd one out: it takes a still, not a sequence,
/// and refuses a stream that is longer than one frame.
/// </remarks>
public static class FrameSink
{
    /// <summary>
    /// Creates a sink that pipes raw frames into a spawned ffmpeg process and encodes a video file.
    /// </summary>
    /// <param name="path">The output video path. Its extension selects the container.</param>
    /// <param name="options">
    /// The encoder configuration, or null for H.264 at <c>-preset slow -crf 16 -pix_fmt yuv420p</c>.
    /// </param>
    /// <returns>An unstarted sink. Dispose it if the run is abandoned before <c>End</c>.</returns>
    /// <remarks>
    /// <para>
    /// This is the delivery path Najm is built around, because it is the only one whose disk cost is
    /// the size of the result. Nothing is written between the renderer and the encoded file, and no
    /// frame is buffered in memory: one pooled lease is filled, written to the pipe, and reused.
    /// </para>
    /// <para>
    /// Select ProRes with <c>new FfmpegPipeOptions { Codec = FfmpegVideoCodec.ProRes }</c> and a
    /// <c>.mov</c> path, which encodes <c>prores_ks -profile:v 3</c>.
    /// </para>
    /// <para>
    /// ffmpeg must be installed and discoverable; it is spawned, never linked. Everything that can
    /// go wrong — a missing binary, a broken pipe, a non-zero exit — fails loudly with ffmpeg's own
    /// diagnostics attached.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    public static FfmpegFrameSink FfmpegPipe(string path, FfmpegPipeOptions? options = null) =>
        new(path, options ?? new FfmpegPipeOptions());

    /// <summary>
    /// Creates a sink that writes every frame to its own numbered PNG file — the non-default path.
    /// </summary>
    /// <param name="directory">The directory the numbered files are written into; created if absent.</param>
    /// <param name="name">The file-name stem. The default is <c>frame</c>.</param>
    /// <returns>An unstarted sink producing <c>{name}_00000.png</c> and upward.</returns>
    /// <remarks>
    /// <para>
    /// <strong>A PNG sequence is very large.</strong> Around 8 MB per 1080p frame and 30 MB per 4K
    /// frame means a twelve-second 4K clip at 60 fps needs roughly thirty gigabytes of free space,
    /// all of which exists only to be fed to an encoder later.
    /// </para>
    /// <para>
    /// <strong>Use <see cref="FfmpegPipe(string, FfmpegPipeOptions?)"/> instead</strong> whenever
    /// the goal is a video. A sequence earns its cost only when the individual frames are what you
    /// need: a frame-by-frame comparison, a hand-off into a compositor, or an encoder Najm does not
    /// drive.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">An argument is null, whitespace, or not a legal file name.</exception>
    public static PngSequenceFrameSink PngSequence(string directory, string name = "frame") =>
        new(directory, name);

    /// <summary>Creates a sink that writes one frame to one named PNG file.</summary>
    /// <param name="path">The PNG file to write. Its directory is created if absent.</param>
    /// <returns>An unstarted sink that accepts exactly one frame.</returns>
    /// <remarks>
    /// <para>
    /// The sink for a still. <see cref="OfflineRenderer.RenderStill"/> produces a one-frame stream
    /// and a still has one destination, so this is not
    /// <see cref="PngSequence(string, string)"/> with a count of one: it takes the whole path rather
    /// than a directory and a stem, and it refuses a stream declaring more than one frame instead of
    /// quietly rewriting the same file per frame.
    /// </para>
    /// <para>
    /// <strong>Most callers want <see cref="SkiaExport.Png"/> instead</strong>, which assembles a
    /// backend, seeks to a time, and renders through this sink in one call. Reach for the sink
    /// directly when driving <see cref="OfflineRenderer.RenderStill"/> yourself — a still with an
    /// explicit output size, a pixel format the export convenience does not expose, or a provider
    /// you already own and are not willing to have built for you.
    /// </para>
    /// <para>
    /// The file is written when the frame is submitted, not when the sink is disposed. A run that
    /// fails before submitting leaves no file, and the sink says so from
    /// <see cref="IFrameSink.End"/> rather than reporting a completed stream over a path that does
    /// not exist.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    public static PngFileFrameSink PngFile(string path) => new(path);
}
