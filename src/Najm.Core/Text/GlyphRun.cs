namespace Najm.Core.Text;

/// <summary>One maximal stretch of a layout's glyphs sharing a face, a size, and a paint.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.4. A run is a <strong>view</strong>, not a container: it names a half-open range of
/// the layout's own glyph, position, and cluster arrays. That is what makes a backend's per-run
/// lowering — one text blob per run — a read over arrays the layout already owns, and what will let
/// a later fragment overlay split a run by materializing new run headers without touching a single
/// element.
/// </para>
/// <para>
/// A plain single-style layout has exactly one run per line. The run vocabulary is nonetheless the
/// contract, because the day a layout carries two faces or two colors is the day a backend that
/// assumed one would have drawn the second in the first one's paint.
/// </para>
/// </remarks>
public sealed class GlyphRun
{
    /// <summary>Creates a run over a range of its layout's arrays.</summary>
    /// <param name="font">The face every glyph in the range belongs to.</param>
    /// <param name="size">The finite positive em size, in local units, the range is positioned at.</param>
    /// <param name="paintIndex">The index into <see cref="ITextLayout.PaintTable"/> this range is painted with.</param>
    /// <param name="start">The index of the range's first glyph in the layout's arrays.</param>
    /// <param name="count">The number of glyphs in the range.</param>
    /// <param name="line">The zero-based line this run sits on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric argument is out of range.</exception>
    public GlyphRun(FontFace font, float size, int paintIndex, int start, int count, int line)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!float.IsFinite(size) || size <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A run's size must be finite and positive.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(paintIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(line);

        Font = font;
        Size = size;
        PaintIndex = paintIndex;
        Start = start;
        Count = count;
        Line = line;
    }

    /// <summary>Gets the face every glyph in this run belongs to.</summary>
    public FontFace Font { get; }

    /// <summary>Gets the em size, in local units, this run's positions were computed at.</summary>
    public float Size { get; }

    /// <summary>Gets the index into <see cref="ITextLayout.PaintTable"/> this run is painted with.</summary>
    public int PaintIndex { get; }

    /// <summary>Gets the index of this run's first glyph in the layout's arrays.</summary>
    public int Start { get; }

    /// <summary>Gets the number of glyphs in this run.</summary>
    public int Count { get; }

    /// <summary>Gets the zero-based line this run sits on.</summary>
    public int Line { get; }
}
