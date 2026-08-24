using Najm.Core.Text;

namespace Najm.Text.Tests.Typesetting;

/// <summary>
/// NAJM-TEXT II.5 and check HB-R1: that the pinned faces really are shaped, not merely mapped
/// character by character.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here is a <em>relationship</em> the shaping must satisfy, not a number read
/// off a previous run: a ligature is fewer glyphs than characters, and a kerned pair is narrower
/// than its two glyphs drawn apart. Both would hold for any correctly shaped font and neither can
/// hold if HarfBuzz is bypassed, so a broken native library or a substituted face fails them.
/// </para>
/// <para>
/// This matters more than it looks. Shipping HB shaping inside the degenerate Latin-only path buys
/// <strong>metric stability</strong>: kerning and ligatures are right from day one, so when
/// multilingual itemization later changes which runs exist, it will not move where a Latin label's
/// glyphs already sit.
/// </para>
/// </remarks>
[TestClass]
public sealed class ShapingTests
{
    private const float Size = 100f;

    [TestMethod]
    public void TheFiLigatureIsOneGlyphCoveringTwoCharacters()
    {
        using var typesetter = new Typesetter();

        var ligated = typesetter.Typeset(Request("fi"));

        // Two characters in, one glyph out: that is what a ligature is. Its single cluster value is
        // 0 — the index of the first character it realizes — which is how a caret and a fragment
        // find their way back to the source through a glyph that is not one-to-one with it.
        Assert.HasCount(1, ligated.Glyphs.ToArray(), "Latin Modern Roman sets 'fi' as one ligature glyph.");
        CollectionAssert.AreEqual(new[] { 0 }, ligated.Clusters.ToArray());
    }

    [TestMethod]
    public void TurningLigaturesOffProducesOneGlyphPerCharacter()
    {
        using var typesetter = new Typesetter();

        var separate = typesetter.Typeset(Request("fi", new FontFeatures { Ligatures = false }));

        // The documented escape from the tracking/ligature tension (II.5): the same two characters
        // now shape to two glyphs with two clusters, one per character.
        Assert.HasCount(2, separate.Glyphs.ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, separate.Clusters.ToArray());
    }

    [TestMethod]
    public void TheLigatureIsNarrowerThanTheTwoGlyphsSetApart()
    {
        using var typesetter = new Typesetter();

        var ligated = typesetter.Typeset(Request("fi")).LogicalBounds.Width;
        var separate = typesetter.Typeset(Request("fi", new FontFeatures { Ligatures = false }))
            .LogicalBounds.Width;

        // Tucking the i's dot under the f's hood is the whole point of the ligature, so it must
        // occupy less width than the two letters do apart. A stack that mapped characters to glyphs
        // without shaping would return the same width for both and fail here.
        Assert.IsLessThan(
            separate,
            ligated,
            $"The 'fi' ligature ({ligated}) must be narrower than 'f'+'i' unligated ({separate}).");
    }

    [TestMethod]
    public void AKernedPairIsTighterThanTheSumOfItsGlyphs()
    {
        using var typesetter = new Typesetter();

        var pair = typesetter.Typeset(Request("AV")).LogicalBounds.Width;
        var apart = typesetter.Typeset(Request("A")).LogicalBounds.Width
            + typesetter.Typeset(Request("V")).LogicalBounds.Width;

        // 'AV' is the classic kern pair: the V's left diagonal tucks under the A's right one. If the
        // kern table were not being applied — no shaping, or a shaper that lost its font — the pair
        // would measure exactly the sum and this would be an equality.
        Assert.IsLessThan(
            apart,
            pair,
            $"'AV' shaped ({pair}) must be tighter than 'A' plus 'V' measured apart ({apart}).");
    }

    [TestMethod]
    public void ClusterValuesIndexTheWholeSourceStringAcrossLines()
    {
        using var typesetter = new Typesetter();

        var layout = typesetter.Typeset(Request("ab\ncd"));

        // Clusters are source indices into the request's own text, not into the line that produced
        // them: 'c' is character 3 of "ab\ncd", and a fragment or caret asking where a glyph came
        // from has to be told that rather than "character 0 of line 1".
        CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, layout.Clusters.ToArray());
    }

    private static TypesetRequest Request(string text, FontFeatures? features = null) =>
        new(text, new Style { Size = Size, Features = features });
}
