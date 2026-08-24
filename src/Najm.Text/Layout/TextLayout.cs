using System.Numerics;
using Najm.Core;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Text.Layout;

/// <summary>The immutable layout <see cref="Typesetter"/> produces and shares.</summary>
/// <remarks>
/// Everything here is fixed at construction. Two nodes showing the same string in the same style
/// hold this same instance, so nothing about it may change afterwards — that sharing is the entire
/// point of the layout cache, and a mutable layout would make one node's edit another node's bug.
/// </remarks>
internal sealed class TextLayout : ITextLayout
{
    private readonly ushort[] glyphs;
    private readonly Vector2[] positions;
    private readonly int[] clusters;
    private readonly GlyphRun[] runs;
    private readonly LineMetrics[] lines;
    private readonly Color[] paintTable;

    internal TextLayout(
        ushort[] glyphs,
        Vector2[] positions,
        int[] clusters,
        GlyphRun[] runs,
        LineMetrics[] lines,
        Color[] paintTable,
        Rect logicalBounds,
        Rect inkBounds)
    {
        this.glyphs = glyphs;
        this.positions = positions;
        this.clusters = clusters;
        this.runs = runs;
        this.lines = lines;
        this.paintTable = paintTable;
        LogicalBounds = logicalBounds;
        InkBounds = inkBounds;
    }

    /// <inheritdoc />
    public Rect LogicalBounds { get; }

    /// <inheritdoc />
    public Rect InkBounds { get; }

    /// <inheritdoc />
    public int LineCount => lines.Length;

    /// <inheritdoc />
    public ReadOnlySpan<ushort> Glyphs => glyphs;

    /// <inheritdoc />
    public ReadOnlySpan<Vector2> Positions => positions;

    /// <inheritdoc />
    public ReadOnlySpan<int> Clusters => clusters;

    /// <inheritdoc />
    public ReadOnlySpan<GlyphRun> Runs => runs;

    /// <inheritdoc />
    public ReadOnlySpan<Color> PaintTable => paintTable;

    /// <inheritdoc />
    public LineMetrics Line(int index)
    {
        if ((uint)index >= (uint)lines.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"This layout has {lines.Length} line(s).");
        }

        return lines[index];
    }
}
