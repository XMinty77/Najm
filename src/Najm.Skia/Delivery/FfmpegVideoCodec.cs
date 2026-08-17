namespace Najm.Skia;

/// <summary>Selects the video encoder <see cref="FfmpegFrameSink"/> asks ffmpeg for.</summary>
/// <remarks>
/// Najm never links ffmpeg; it spawns the binary and writes raw frames to its standard input, so
/// this enumeration is a curated shortlist of encoders known to be present in the pinned
/// environment rather than a wrapper over ffmpeg's full codec table. Anything else is reachable
/// through <see cref="FfmpegPipeOptions.ExtraArguments"/>.
/// </remarks>
public enum FfmpegVideoCodec
{
    /// <summary>
    /// <c>libx264</c> into a widely playable H.264 stream. The default, and what an MP4 delivery
    /// wants.
    /// </summary>
    /// <remarks>
    /// Encoded with <c>-preset slow -crf 16 -pix_fmt yuv420p</c> by default: visually lossless for
    /// motion graphics, and 4:2:0 because that is what players and browsers accept. Chroma
    /// subsampling requires even frame dimensions, which the sink checks before the first frame.
    /// </remarks>
    H264,

    /// <summary>
    /// <c>prores_ks</c> for an intermediate master destined for an editor rather than a viewer.
    /// </summary>
    /// <remarks>
    /// Encoded at <c>-profile:v 3</c> (ProRes 422 HQ) with <c>-pix_fmt yuv422p10le</c>: ten-bit,
    /// far larger files, no rate factor. Give the sink a <c>.mov</c> path — the muxer is chosen from
    /// the output extension.
    /// </remarks>
    ProRes,
}
