using Najm.Core.Text;

namespace Najm.Text.Tests.Typesetting;

/// <summary>NAJM-TEXT II.7: where the lines go, how tall they are, and how wide the box is.</summary>
/// <remarks>
/// Every number below is derived from <see cref="ITypesetter.Metrics"/> or from a measured advance
/// read back from the layout under test, never from a captured run. That is deliberate: a golden
/// number would pin the pinned font's metrics rather than the arithmetic this file is about, and
/// would have to be re-captured — unexaminably — if the bundled bytes were ever revised.
/// </remarks>
[TestClass]
public sealed class LineLayoutTests
{
    private const float Size = 100f;

    [TestMethod]
    public void MetricsScaleExactlyLinearlyWithSize()
    {
        using var typesetter = new Typesetter();
        var face = FaceOf(typesetter, "H");

        var small = typesetter.Metrics(face, 12f);
        var large = typesetter.Metrics(face, 24f);

        // Nothing in this stack hints (II.5), so metrics at 24 are exactly twice metrics at 12 —
        // exactly, not nearly. A hinted stack would round each to the pixel grid and break it, which
        // is precisely why "one shaped entry serves every size" would stop being true.
        Assert.AreEqual(small.Ascent * 2f, large.Ascent, 0f);
        Assert.AreEqual(small.Descent * 2f, large.Descent, 0f);
        Assert.AreEqual(small.LineGap * 2f, large.LineGap, 0f);
        Assert.AreEqual(small.LineHeight * 2f, large.LineHeight, 0f);
    }

    [TestMethod]
    public void AscentAndDescentAreReportedAsPositiveMagnitudes()
    {
        using var typesetter = new Typesetter();

        var metrics = typesetter.Metrics(FaceOf(typesetter, "H"), Size);

        // I.2's rule at the portable boundary. The font file and HarfBuzz both call the descender
        // negative; an engine that passed that sign around would eventually add where it meant to
        // subtract, and the bug would be one line of text a descender too high.
        Assert.IsGreaterThan(0f, metrics.Ascent);
        Assert.IsGreaterThan(0f, metrics.Descent);
        Assert.IsGreaterThanOrEqualTo(0f, metrics.LineGap);
    }

    [TestMethod]
    public void BaselinesStackByTheFaceLineHeightTimesLineSpacing()
    {
        using var typesetter = new Typesetter();
        var metrics = typesetter.Metrics(FaceOf(typesetter, "H"), Size);
        const float Spacing = 1.5f;
        var expectedAdvance = metrics.LineHeight * Spacing;

        var layout = typesetter.Typeset(new TypesetRequest("one\ntwo\nthree", Style())
        {
            LineSpacing = Spacing,
        });

        Assert.AreEqual(3, layout.LineCount);
        for (var line = 0; line < layout.LineCount; line++)
        {
            // Line 0's baseline is the layout origin by construction, and every later baseline is
            // that many advances down the reading frame.
            Assert.AreEqual(
                line * expectedAdvance,
                layout.Line(line).Baseline,
                0.001f,
                $"Line {line}'s baseline must sit {line} × (ascent+descent+lineGap) × {Spacing} below the first.");
        }
    }

    [TestMethod]
    public void LogicalBoundsAreTheBlockBoxByTheLineBand()
    {
        using var typesetter = new Typesetter();
        var metrics = typesetter.Metrics(FaceOf(typesetter, "H"), Size);

        var layout = typesetter.Typeset(new TypesetRequest("one\ntwo", Style()));
        var lastBaseline = layout.Line(layout.LineCount - 1).Baseline;
        var widest = MathF.Max(layout.Line(0).Width, layout.Line(1).Width);

        // The logical box runs from the first line's ascent to the last line's descent, and spans
        // the widest line. It is a metric box, not an ink box: two labels at one size are the same
        // height whether or not either happens to have a descender in it.
        Assert.AreEqual(0f, layout.LogicalBounds.Left, 0f);
        Assert.AreEqual(widest, layout.LogicalBounds.Width, 0.001f);
        Assert.AreEqual(-metrics.Ascent, layout.LogicalBounds.Top, 0.001f);
        Assert.AreEqual(lastBaseline + metrics.Descent, layout.LogicalBounds.Bottom, 0.001f);
    }

    [TestMethod]
    public void AlignmentPlacesEachLineInsideTheBlockBox()
    {
        using var typesetter = new Typesetter();

        var left = typesetter.Typeset(new TypesetRequest("iiii\nMMMM", Style()) { Align = TextAlign.Left });
        var center = typesetter.Typeset(new TypesetRequest("iiii\nMMMM", Style()) { Align = TextAlign.Center });
        var right = typesetter.Typeset(new TypesetRequest("iiii\nMMMM", Style()) { Align = TextAlign.Right });

        var block = left.LogicalBounds.Width;
        var narrow = left.Line(0).Width;
        var wide = left.Line(1).Width;
        Assert.IsLessThan(wide, narrow, "'iiii' must be narrower than 'MMMM' for this test to say anything.");
        Assert.AreEqual(wide, block, 0.001f, "With no MaxWidth the alignment box is the widest line.");

        // Left pins every line at the box's left edge; right pins every line's end at its right
        // edge; centre splits the slack. The wide line has no slack under any of the three.
        Assert.AreEqual(0f, left.Line(0).Left, 0.001f);
        Assert.AreEqual(0f, left.Line(1).Left, 0.001f);
        Assert.AreEqual((block - narrow) * 0.5f, center.Line(0).Left, 0.001f);
        Assert.AreEqual(0f, center.Line(1).Left, 0.001f);
        Assert.AreEqual(block - narrow, right.Line(0).Left, 0.001f);
        Assert.AreEqual(0f, right.Line(1).Left, 0.001f);
    }

    [TestMethod]
    public void HardBreaksAreTheOnlyBreaksAndAllThreeSpellingsCount()
    {
        using var typesetter = new Typesetter();

        // n breaks make n+1 lines, with no special case for a trailing one. All three UAX #14
        // mandatory-break spellings are recognized, so a string pasted from a Windows editor lays
        // out the same as one typed here instead of shaping a stray carriage return into a box.
        Assert.AreEqual(2, LineCount(typesetter, "a\nb"));
        Assert.AreEqual(2, LineCount(typesetter, "a\r\nb"));
        Assert.AreEqual(2, LineCount(typesetter, "a\rb"));
        Assert.AreEqual(2, LineCount(typesetter, "a\n"));
        Assert.AreEqual(3, LineCount(typesetter, "a\n\nb"));
        Assert.AreEqual(1, LineCount(typesetter, "a b c"));
        Assert.AreEqual(1, LineCount(typesetter, string.Empty));
    }

    [TestMethod]
    public void EmptyTextStillHasOneLineWithTheFaceHeight()
    {
        using var typesetter = new Typesetter();
        var metrics = typesetter.Metrics(FaceOf(typesetter, "H"), Size);

        var layout = typesetter.Typeset(new TypesetRequest(string.Empty, Style()));

        // A label whose text has just been cleared still occupies a line: its box collapses in x and
        // keeps the face's height, so a caret or an underline placed against it does not jump to the
        // origin and back as the text empties and refills.
        Assert.AreEqual(1, layout.LineCount);
        Assert.IsEmpty(layout.Glyphs.ToArray());
        Assert.IsEmpty(layout.Runs.ToArray());
        Assert.AreEqual(0f, layout.LogicalBounds.Width, 0f);
        Assert.AreEqual(metrics.Ascent, layout.Line(0).Ascent, 0.001f);
        Assert.AreEqual(metrics.Descent, layout.Line(0).Descent, 0.001f);
    }

    [TestMethod]
    public void LetterSpacingIsAddedOncePerClusterIncludingTheLast()
    {
        using var typesetter = new Typesetter();
        const float Tracking = 7f;

        var plain = typesetter.Typeset(new TypesetRequest("abcd", Style()));
        var tracked = typesetter.Typeset(new TypesetRequest("abcd", Style(Tracking)));

        // Four one-glyph clusters take four tracks, the trailing one included — the CSS
        // letter-spacing rule. Skipping the last would make a line's width depend on where it ends
        // and would put centred tracked text half a track left of where every other tool puts it.
        Assert.AreEqual(
            plain.LogicalBounds.Width + (4f * Tracking),
            tracked.LogicalBounds.Width,
            0.001f);
    }

    [TestMethod]
    public void LetterSpacingNeverOpensALigature()
    {
        using var typesetter = new Typesetter();
        const float Tracking = 7f;
        var noLigatures = new FontFeatures { Ligatures = false };

        var ligated = typesetter.Typeset(new TypesetRequest("fi", Style()));
        var ligatedTracked = typesetter.Typeset(new TypesetRequest("fi", Style(Tracking)));
        var separate = typesetter.Typeset(new TypesetRequest("fi", Style(features: noLigatures)));
        var separateTracked = typesetter.Typeset(
            new TypesetRequest("fi", Style(Tracking, noLigatures)));

        // II.5's rule falls straight out of "tracking lands at cluster ends": the ligature is one
        // cluster, so it takes one track and none in its middle; the same two characters unligated
        // are two clusters and take two. Tracked text with ligatures on is therefore tighter than
        // tracked text without — the documented trade-off, measurable rather than asserted.
        Assert.AreEqual(ligated.LogicalBounds.Width + Tracking, ligatedTracked.LogicalBounds.Width, 0.001f);
        Assert.AreEqual(separate.LogicalBounds.Width + (2f * Tracking), separateTracked.LogicalBounds.Width, 0.001f);
    }

    [TestMethod]
    public void InkBoundsAreTightWhereLogicalBoundsAreMetric()
    {
        using var typesetter = new Typesetter();

        var layout = typesetter.Typeset(new TypesetRequest("H", Style()));
        var ink = layout.InkBounds;
        var logical = layout.LogicalBounds;

        // 'H' is a flat-topped capital with no descender and a positive side bearing, so its ink is
        // strictly inside its advance horizontally, sits on the baseline exactly, and reaches only
        // to the cap height rather than to the face's ascent.
        Assert.IsGreaterThan(logical.Left, ink.Left, "The left side bearing must leave ink clear of the pen.");
        Assert.IsLessThan(logical.Right, ink.Right, "The right side bearing must leave ink clear of the advance.");
        Assert.AreEqual(0f, ink.Bottom, 0.001f, "'H' has no descender, so its ink ends on the baseline.");
        Assert.IsGreaterThan(logical.Top, ink.Top, "Cap height is below the face's ascent.");
    }

    private static int LineCount(ITypesetter typesetter, string text) =>
        typesetter.Typeset(new TypesetRequest(text, Style())).LineCount;

    private static Style Style(float? letterSpacing = null, FontFeatures? features = null) =>
        new() { Size = Size, LetterSpacing = letterSpacing, Features = features };

    private static FontFace FaceOf(ITypesetter typesetter, string probe) =>
        typesetter.Typeset(new TypesetRequest(probe, Style())).Runs[0].Font;
}
