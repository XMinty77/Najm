using HarfBuzzSharp;

namespace Najm.Text.HarfBuzz;

internal sealed class HarfBuzzShaper : IDisposable
{
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private HarfBuzzFaceEntry? face;
    private HarfBuzzBufferPool? buffers = new();

    internal HarfBuzzShaper(ReadOnlyMemory<byte> fontBytes, int faceIndex = 0)
    {
        face = new HarfBuzzFaceEntry(fontBytes, faceIndex);
    }

    internal int UnitsPerEm
    {
        get
        {
            EnsureUsable();
            return face!.UnitsPerEm;
        }
    }

    internal ShapedRun Shape(
        ReadOnlySpan<char> text,
        Direction direction,
        Script script,
        Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        EnsureUsable();

        var buffer = buffers!.Rent();
        try
        {
            buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
            buffer.Direction = direction;
            buffer.Script = script;
            buffer.Language = language;
            buffer.AddUtf16(text);
            face!.Font.Shape(buffer);

            var infos = buffer.GetGlyphInfoSpan();
            var positions = buffer.GetGlyphPositionSpan();
            if (infos.Length != positions.Length)
            {
                throw new InvalidOperationException("HarfBuzz returned mismatched glyph and position counts.");
            }

            var glyphs = GC.AllocateUninitializedArray<ShapedGlyph>(infos.Length);
            var totalXAdvance = 0;
            for (var index = 0; index < infos.Length; index++)
            {
                var info = infos[index];
                var position = positions[index];
                totalXAdvance = checked(totalXAdvance + position.XAdvance);
                glyphs[index] = new ShapedGlyph(
                    info.Codepoint,
                    info.Cluster,
                    position.XAdvance,
                    position.YAdvance,
                    position.XOffset,
                    position.YOffset);
            }

            return new ShapedRun(glyphs, totalXAdvance);
        }
        finally
        {
            buffers.Return(buffer);
        }
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        buffers?.Dispose();
        buffers = null;
        face?.Dispose();
        face = null;
    }

    private void EnsureUsable()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(face is null || buffers is null, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
        {
            throw new InvalidOperationException(
                "A HarfBuzz shaper must be used and disposed on the thread that created it.");
        }
    }
}
