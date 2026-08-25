using Najm.Core;

namespace Najm.Skia;

/// <summary>Measures and compares rendered image files — the reading end of the delivery seam.</summary>
/// <remarks>
/// <para>
/// <see cref="FrameSink"/> writes frames out; this reads them back and says what is in them.
/// Together they close the grading loop — render, measure, look — entirely inside the engine.
/// Before this existed, answering "how many pixels are clipped, and what is the p90" meant decoding
/// PNGs by hand outside Najm, which two separate productions ended up doing.
/// </para>
/// <para>
/// <b>Why the file-taking overloads live here and the arithmetic does not.</b>
/// <see cref="FrameStats"/> and <see cref="FrameDifference"/> are in <c>Najm.Core</c> because
/// counting levels in a decoded buffer needs no backend. Producing that buffer from a PNG needs a
/// codec, so this thin layer — decode, then delegate — is the only part that had to be here.
/// </para>
/// <para>
/// <b>Format.</b> The default is <see cref="PixelFormat.Rgba8888"/>, straight alpha, because that
/// is what a PNG actually stores; asking for a premultiplied format is a real conversion, not a
/// relabelling. Both sides of a comparison are decoded the same way, so a comparison is always
/// like for like.
/// </para>
/// <para>
/// This is diagnostic and offline. It touches the disk and reduces whole frames; nothing in a warm
/// render path calls it, and nothing should.
/// </para>
/// </remarks>
public static class FrameProbe
{
    /// <summary>Decodes an image file into a leased frame the caller owns and must dispose.</summary>
    /// <param name="path">The image file to decode.</param>
    /// <param name="format">The byte and alpha layout to decode into.</param>
    /// <returns>A rented <see cref="PixelFrameLease"/>; dispose it to return its memory to the pool.</returns>
    /// <remarks>
    /// Use this when several questions will be asked of the same file, or when the pixels are needed
    /// for something other than a measurement. For a single question, <see cref="Measure"/> and
    /// <see cref="Compare"/> handle the lease themselves.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">Skia could not decode the file.</exception>
    public static PixelFrameLease Read(string path, PixelFormat format = PixelFormat.Rgba8888) =>
        SkiaPngReader.Read(path, format);

    /// <summary>Decodes an image file and measures it.</summary>
    /// <param name="path">The image file to measure.</param>
    /// <param name="format">The byte and alpha layout to decode into before measuring.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">Skia could not decode the file.</exception>
    public static FrameStats Measure(string path, PixelFormat format = PixelFormat.Rgba8888)
    {
        using var pixels = Read(path, format);
        return FrameStats.Of(pixels);
    }

    /// <summary>Decodes two image files and reports how their pixels differ.</summary>
    /// <param name="path">The image under test.</param>
    /// <param name="referencePath">The image it is being held against.</param>
    /// <param name="format">The byte and alpha layout both are decoded into.</param>
    /// <remarks>
    /// This is the report for when <see cref="AreIdentical"/> has already said no. It refuses files
    /// of different sizes loudly, because a difference report over mismatched geometry would be a
    /// number with no meaning.
    /// </remarks>
    /// <exception cref="ArgumentException">A path is null or whitespace, or the images differ in size.</exception>
    /// <exception cref="FileNotFoundException">One of the files does not exist.</exception>
    /// <exception cref="InvalidDataException">Skia could not decode one of the files.</exception>
    public static FrameDifference Compare(
        string path,
        string referencePath,
        PixelFormat format = PixelFormat.Rgba8888)
    {
        using var pixels = Read(path, format);
        using var reference = Read(referencePath, format);
        return FrameComparison.Between(pixels, reference);
    }

    /// <summary>Answers whether two image files hold pixel-identical images.</summary>
    /// <param name="path">The image under test.</param>
    /// <param name="referencePath">The image it is being held against.</param>
    /// <param name="format">The byte and alpha layout both are decoded into.</param>
    /// <returns>True when both decode to the same size and the same bytes.</returns>
    /// <remarks>
    /// <b>Pixel-identical, not file-identical.</b> Two PNGs holding the same image can differ byte
    /// for byte on disk — a different encoder, a different compression level, a text chunk, a
    /// timestamp — so comparing the files themselves answers a question nobody asked. Differently
    /// sized images return false rather than throwing; they are not the same image, which is the
    /// answer requested.
    /// </remarks>
    /// <exception cref="ArgumentException">A path is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">One of the files does not exist.</exception>
    /// <exception cref="InvalidDataException">Skia could not decode one of the files.</exception>
    public static bool AreIdentical(
        string path,
        string referencePath,
        PixelFormat format = PixelFormat.Rgba8888)
    {
        using var pixels = Read(path, format);
        using var reference = Read(referencePath, format);
        return FrameComparison.AreIdentical(pixels, reference);
    }
}
