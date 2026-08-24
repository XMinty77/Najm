using HarfBuzzSharp;
using Najm.Core.Text;
using Najm.Text.HarfBuzz;

namespace Najm.Text;

/// <summary>The typesetter's per-face side table: one shaper, its metrics, and its ink boxes.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.2 and II.1: native realizations never live on the portable
/// <see cref="FontFace"/> handle. This is where <c>Najm.Text</c> keeps its half of that split,
/// keyed by handle identity and lazily created on first use, so registering a family costs nothing
/// until something is actually shaped with it. A backend keeps its own, independent side table
/// keyed by the same handle, and neither can see the other's.
/// </para>
/// <para>
/// <strong>Font-unit metrics, cached once.</strong> Extents and glyph ink boxes are read in font
/// units — the shaper's scale is <c>(upem, upem)</c> — and scaled per call by <c>size / upem</c>.
/// Nothing hints, so that scaling is exact rather than an approximation of a hinted result, and one
/// cached font-unit box serves every size the face is ever drawn at.
/// </para>
/// </remarks>
internal sealed class FaceEntry : IDisposable
{
    private readonly Dictionary<uint, InkBox> inkBoxes = [];
    private HarfBuzzShaper? shaper;

    internal FaceEntry(FontFace face)
    {
        Face = face;
        shaper = new HarfBuzzShaper(face.Bytes, face.FaceIndex);
        UnitsPerEm = shaper.UnitsPerEm;

        if (shaper.TryGetFontExtents(out var ascent, out var descent, out var lineGap))
        {
            Ascent = ascent;
            Descent = descent;
            LineGap = lineGap;
        }
        else
        {
            // HB-R2's degrade, stated: a face that reports no horizontal extents still has an em
            // box, and the classic 80/20 split of it is a correct line height rather than a zero
            // one. Deterministic, and visibly a fallback if it ever fires for a bundled face.
            Ascent = (int)(UnitsPerEm * 0.8);
            Descent = UnitsPerEm - Ascent;
            LineGap = 0;
        }
    }

    /// <summary>One glyph's ink box in font units, in the reading frame.</summary>
    internal readonly record struct InkBox(int Left, int Top, int Width, int Height);

    internal FontFace Face { get; }

    internal int UnitsPerEm { get; }

    internal int Ascent { get; }

    internal int Descent { get; }

    internal int LineGap { get; }

    internal HarfBuzzShaper Shaper =>
        shaper ?? throw new ObjectDisposedException(nameof(FaceEntry));

    /// <summary>Reads one glyph's font-unit ink box, caching it on first use.</summary>
    internal InkBox GlyphInkBox(uint glyphId)
    {
        if (inkBoxes.TryGetValue(glyphId, out var cached))
        {
            return cached;
        }

        var box = Shaper.TryGetGlyphInkBox(glyphId, out var left, out var top, out var width, out var height)
            ? new InkBox(left, top, width, height)
            : default;
        inkBoxes[glyphId] = box;
        return box;
    }

    /// <summary>Shapes one run in font units.</summary>
    internal ShapedRun Shape(ReadOnlySpan<char> text, Direction direction, Script script, Language language, Feature[] features) =>
        Shaper.Shape(text, direction, script, language, features);

    public void Dispose()
    {
        shaper?.Dispose();
        shaper = null;
    }
}
