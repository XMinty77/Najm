using System.Numerics;
using Najm.Utils;

namespace Najm.Core.Text;

/// <summary>One finished, immutable, backend-neutral text layout.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.4. A layout is portable data: glyph ids, positions, cluster values, run views, a
/// paint table, and measured bounds. It contains <strong>no backend object of any kind</strong> — no
/// text blob, no native path, and no writable slot a backend could park one in. A backend that wants
/// a native realization keeps it in a side table of its own, keyed by the layout, and the layout
/// never learns that it happened.
/// </para>
/// <para>
/// <strong>The reading frame.</strong> The origin is the first line's baseline pen at the alignment
/// box's left edge; <c>+x</c> follows the baseline and <c>+y</c> points toward descenders. This is a
/// Y-down frame whichever way the layer that eventually draws it points, which is exactly what makes
/// the upright rule (I.9) a node-side composition rather than a typesetting parameter.
/// </para>
/// <para>
/// <strong>Immutable after construction</strong>, arrays included. Layouts are shared: two nodes
/// showing the same string in the same style hold the same instance, and one of them mutating it
/// would corrupt the other.
/// </para>
/// <para>
/// <strong>What this slice does not carry.</strong> I.4's full surface also has <c>Rules</c> for
/// underline and strikethrough, <c>Pictures</c> for math and external TeX, a <c>Fragments</c> table
/// with its <c>Generation</c>, the <c>IndexToX</c>/<c>XToIndex</c> caret floor, and
/// <c>BakePath</c>. Each belongs to a feature this slice does not build, and each is absent rather
/// than present-and-empty so that no caller can write code against a member that has never once
/// returned anything.
/// </para>
/// </remarks>
public interface ITextLayout
{
    /// <summary>Gets the layout's logical box in the reading frame: the alignment box by the line band.</summary>
    /// <remarks>
    /// This is the metric box, driven by font metrics rather than by ink, so two labels of the same
    /// size have the same height whether or not either has a descender. It is what box anchors
    /// reference and what a text node reports as its geometry.
    /// </remarks>
    Rect LogicalBounds { get; }

    /// <summary>Gets the tight union of the drawn glyph outlines, in the reading frame.</summary>
    /// <remarks>
    /// Ink can fall outside <see cref="LogicalBounds"/> — an italic's overhang, an accent above the
    /// ascent — so this is what a conservative visual bound is built from.
    /// </remarks>
    Rect InkBounds { get; }

    /// <summary>Gets the number of lines, which is at least one even for empty text.</summary>
    int LineCount { get; }

    /// <summary>Reads one line's metrics.</summary>
    /// <param name="index">The zero-based line index.</param>
    /// <returns>Where the line sits and how tall it is, in the reading frame.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the layout.</exception>
    LineMetrics Line(int index);

    /// <summary>Gets every glyph id, in visual order.</summary>
    ReadOnlySpan<ushort> Glyphs { get; }

    /// <summary>Gets each glyph's pen position in the reading frame, parallel to <see cref="Glyphs"/>.</summary>
    ReadOnlySpan<Vector2> Positions { get; }

    /// <summary>
    /// Gets each glyph's cluster value — the index in <see cref="TypesetRequest.Text"/> of the first
    /// character it realizes — parallel to <see cref="Glyphs"/>.
    /// </summary>
    /// <remarks>
    /// Several glyphs may share a cluster and one glyph may cover several characters: that is what a
    /// ligature is. Cluster values are monotone in logical order, which is what makes them usable as
    /// a caret and selection map.
    /// </remarks>
    ReadOnlySpan<int> Clusters { get; }

    /// <summary>Gets the run views over the arrays above, in draw order.</summary>
    ReadOnlySpan<GlyphRun> Runs { get; }

    /// <summary>Gets the resolved colors runs index into by <see cref="GlyphRun.PaintIndex"/>.</summary>
    /// <remarks>
    /// A single-style layout has one entry. A draw-time color override replaces the table for the
    /// whole layout without re-typesetting it, which is what makes recoloring and color tweens free.
    /// </remarks>
    ReadOnlySpan<Color> PaintTable { get; }
}
