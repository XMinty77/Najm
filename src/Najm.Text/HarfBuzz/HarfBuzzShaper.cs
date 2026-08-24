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

    /// <summary>
    /// Reads the face's horizontal font extents in font units, as positive ascent and descent
    /// magnitudes.
    /// </summary>
    /// <remarks>
    /// HarfBuzz reports the descender as a negative number, following the font file. NAJM-TEXT I.2
    /// requires positive magnitudes at the portable boundary, and this is the one place the sign is
    /// flipped, so nothing downstream has to remember which convention it is holding.
    /// </remarks>
    /// <returns>Whether the face reports horizontal extents at all.</returns>
    internal bool TryGetFontExtents(out int ascent, out int descent, out int lineGap)
    {
        EnsureUsable();
        if (face!.Font.TryGetHorizontalFontExtents(out var extents))
        {
            ascent = Math.Abs(extents.Ascender);
            descent = Math.Abs(extents.Descender);
            lineGap = extents.LineGap;
            return true;
        }

        ascent = 0;
        descent = 0;
        lineGap = 0;
        return false;
    }

    /// <summary>Reads one glyph's ink box in font units, in the reading frame.</summary>
    /// <remarks>
    /// HarfBuzz reports extents in its own y-up convention: <c>YBearing</c> is the top edge above
    /// the baseline and <c>Height</c> is negative downward. This returns the box already converted
    /// to the reading frame, where +y points toward descenders, so the caller adds it to a pen
    /// position directly.
    /// </remarks>
    /// <returns>Whether the face reports extents for that glyph. A blank glyph reports an empty box.</returns>
    internal bool TryGetGlyphInkBox(uint glyphId, out int left, out int top, out int width, out int height)
    {
        EnsureUsable();
        if (face!.Font.TryGetGlyphExtents(glyphId, out var extents))
        {
            left = extents.XBearing;
            top = -extents.YBearing;
            width = Math.Abs(extents.Width);
            height = Math.Abs(extents.Height);
            return true;
        }

        left = 0;
        top = 0;
        width = 0;
        height = 0;
        return false;
    }

    internal ShapedRun Shape(
        ReadOnlySpan<char> text,
        Direction direction,
        Script script,
        Language language) =>
        Shape(text, direction, script, language, []);

    /// <summary>Shapes one run with an explicit OpenType feature array.</summary>
    /// <remarks>
    /// An empty array is HarfBuzz's own default set, which already has standard ligatures and
    /// kerning on; a caller that wants exactly that passes nothing and allocates nothing. The array
    /// is a shaping input, so callers that cache shaped runs must include it in their key.
    /// </remarks>
    internal ShapedRun Shape(
        ReadOnlySpan<char> text,
        Direction direction,
        Script script,
        Language language,
        Feature[] features)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(features);
        EnsureUsable();

        var buffer = buffers!.Rent();
        try
        {
            buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
            buffer.Direction = direction;
            buffer.Script = script;
            buffer.Language = language;
            buffer.AddUtf16(text);
            face!.Font.Shape(buffer, features);

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
