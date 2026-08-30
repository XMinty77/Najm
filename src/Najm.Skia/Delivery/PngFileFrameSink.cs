using Najm.Core;

namespace Najm.Skia;

/// <summary>Writes a one-frame stream to a single named PNG file.</summary>
/// <remarks>
/// <para>
/// This backs <see cref="SkiaExport.Png"/>. A still export has one destination path rather than a
/// numbering scheme, so it is deliberately not the sequence sink with a count of one — and it
/// enforces that: a stream declaring more than one frame is refused at
/// <see cref="Begin"/> rather than silently overwriting the file per frame, and a stream that
/// declares one and submits none is refused at <see cref="End"/> rather than reporting success over
/// a file that was never written.
/// </para>
/// <para>
/// <strong>Reach for it through <see cref="FrameSink.PngFile(string)"/></strong> when driving
/// <see cref="OfflineRenderer.RenderStill"/> yourself. The alternative authors were left with — a
/// <see cref="FrameSink.PngSequence(string, string)"/> into a scratch directory, a
/// <c>File.Move</c> of <c>still_00000.png</c>, and a <c>try/finally</c> that deletes the
/// directory — is eleven lines that exist only because this type used to be internal.
/// </para>
/// </remarks>
public sealed class PngFileFrameSink : IFrameSink
{
    private readonly string path;
    private bool begun;
    private bool written;
    private bool ended;

    internal PngFileFrameSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Fully qualified because this type's own Path property shadows System.IO.Path, and
        // resolved eagerly so the property, the failure messages, and the file all name one place
        // even if the process changes directory mid-render.
        this.path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>Gets the absolute path the single frame is written to.</summary>
    public string Path => path;

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
