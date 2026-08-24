namespace Najm.Core.Text;

/// <summary>Where one line of a layout sits and how tall it is, in the reading frame.</summary>
/// <remarks>
/// <para>
/// Every value is in the layout's reading frame — <c>+x</c> along the baseline, <c>+y</c> toward
/// descenders — which is the frame the layout's glyph positions are in. A text node maps them into
/// its own local space through the upright rule (I.9); a caller reading these directly is reading
/// the layout, not the node.
/// </para>
/// <para>
/// <see cref="Ascent"/> and <see cref="Descent"/> are positive magnitudes, so the line's ink band is
/// <c>[Baseline − Ascent, Baseline + Descent]</c>.
/// </para>
/// </remarks>
/// <param name="Baseline">The line's baseline y. Line 0's is 0 by construction.</param>
/// <param name="Left">The x of the line's first pen, after alignment.</param>
/// <param name="Width">The line's advance width, including any trailing letter spacing.</param>
/// <param name="Ascent">How far this line rises above its baseline, positive.</param>
/// <param name="Descent">How far this line falls below its baseline, positive.</param>
/// <param name="GlyphStart">The index of this line's first glyph in the layout's arrays.</param>
/// <param name="GlyphCount">The number of glyphs on this line.</param>
/// <param name="SourceStart">The index in <see cref="TypesetRequest.Text"/> this line starts at.</param>
/// <param name="SourceLength">The number of source characters on this line, excluding its break.</param>
public readonly record struct LineMetrics(
    float Baseline,
    float Left,
    float Width,
    float Ascent,
    float Descent,
    int GlyphStart,
    int GlyphCount,
    int SourceStart,
    int SourceLength)
{
    /// <summary>Gets the line's logical box in the reading frame.</summary>
    public Rect LogicalBounds => new(Left, Baseline - Ascent, Width, Ascent + Descent);
}
