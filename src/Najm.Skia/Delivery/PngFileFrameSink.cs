using Najm.Core;

namespace Najm.Skia;

/// <summary>Writes a one-frame stream to a single named PNG file.</summary>
/// <remarks>
/// This backs <see cref="SkiaExport.Png"/>. A still export has one destination path rather than a
/// numbering scheme, so it is deliberately not the sequence sink with a count of one.
/// </remarks>
internal sealed class PngFileFrameSink : IFrameSink
{
    private readonly string path;
    private bool begun;
    private bool written;
    private bool ended;

    internal PngFileFrameSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    /// <inheritdoc />
    public void Begin(in FrameStreamInfo info)
    {
        if (begun)
        {
            throw new InvalidOperationException("This PNG file sink has already begun a stream.");
        }
        if (!info.IsValid)
        {
            throw new ArgumentException("A frame stream description is required.", nameof(info));
        }
        if (info.FrameCount is not (null or 1L))
        {
            throw new InvalidOperationException(
                $"A PNG file sink writes exactly one frame; the stream declared {info.FrameCount}. " +
                $"Use {nameof(FrameSink)}.{nameof(FrameSink.PngSequence)} for a numbered sequence.");
        }

        begun = true;
    }

    /// <inheritdoc />
    public void Submit(long frame, PixelFrameLease pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        using (pixels)
        {
            if (!begun || ended)
            {
                throw new InvalidOperationException("This PNG file sink is not accepting frames.");
            }
            if (written)
            {
                throw new InvalidOperationException(
                    $"A PNG file sink writes exactly one frame; frame {frame} is the second.");
            }

            SkiaPngWriter.Write(pixels, path);
            written = true;
        }
    }

    /// <inheritdoc />
    public void End()
    {
        if (!begun)
        {
            throw new InvalidOperationException("This PNG file sink has not begun a stream.");
        }
        if (ended)
        {
            throw new InvalidOperationException("This PNG file sink has already ended its stream.");
        }
        if (!written)
        {
            throw new InvalidOperationException(
                $"The still render submitted no frame, so '{path}' was never written.");
        }

        ended = true;
    }
}
