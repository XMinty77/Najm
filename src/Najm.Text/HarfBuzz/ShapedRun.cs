namespace Najm.Text.HarfBuzz;

internal readonly record struct ShapedGlyph(
    uint GlyphId,
    uint Cluster,
    int XAdvance,
    int YAdvance,
    int XOffset,
    int YOffset);

internal sealed class ShapedRun
{
    private readonly ShapedGlyph[] glyphs;

    internal ShapedRun(ShapedGlyph[] glyphs, int totalXAdvance)
    {
        this.glyphs = glyphs;
        TotalXAdvance = totalXAdvance;
    }

    internal ReadOnlySpan<ShapedGlyph> Glyphs => glyphs;

    internal int TotalXAdvance { get; }
}
