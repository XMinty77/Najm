using Najm.Core.Text;

namespace Najm.Text.Tests.Typesetting;

/// <summary>NAJM-TEXT I.13 and II.5: what the caches key on, and what that buys.</summary>
[TestClass]
public sealed class CacheAndDeterminismTests
{
    private const float Size = 100f;

    [TestMethod]
    public void IdenticalRequestsReturnTheVerySameLayoutInstance()
    {
        using var typesetter = new Typesetter();

        var first = typesetter.Typeset(new TypesetRequest("2.0", Style()));
        var second = typesetter.Typeset(new TypesetRequest("2.0", Style()));

        // Not "equal geometry" — the same object. This is the dedup the whole cache design exists
        // for: forty tick labels over twenty-one distinct strings hold twenty-one layouts between
        // them, and a repeated label costs a dictionary probe.
        Assert.AreSame(first, second);
        Assert.AreEqual(1, typesetter.CachedLayoutCount);
    }

    [TestMethod]
    public void OneShapedRunServesEverySize()
    {
        using var typesetter = new Typesetter();

        foreach (var size in new[] { 8f, 12f, 24f, 48f, 96f })
        {
            typesetter.Typeset(new TypesetRequest("Najm", new Style { Size = size }));
        }

        // "The single highest-leverage cache decision in the stack" (II.5), measured. Shaping runs
        // at scale = (upem, upem), so its output is a font-unit fact with no size in it; only line
        // layout scales. Five sizes are therefore five layouts over one shaped run — which is also
        // why a size tween, the documented anti-idiom, still reshapes nothing.
        Assert.AreEqual(1, typesetter.CachedShapedRunCount, "Shaping must be size-independent.");
        Assert.AreEqual(5, typesetter.CachedLayoutCount, "Size is a layout cache key.");
    }

    [TestMethod]
    public void SizeScalesTheLayoutExactly()
    {
        using var typesetter = new Typesetter();

        var small = typesetter.Typeset(new TypesetRequest("Najm", new Style { Size = 50f }));
        var large = typesetter.Typeset(new TypesetRequest("Najm", new Style { Size = 100f }));

        // The other half of the same claim: one shaped run means the two layouts are the same
        // numbers times a constant, so doubling the size doubles every advance and every position.
        Assert.AreEqual(small.LogicalBounds.Width * 2f, large.LogicalBounds.Width, 0.001f);
        for (var index = 0; index < small.Glyphs.Length; index++)
        {
            Assert.AreEqual(small.Glyphs[index], large.Glyphs[index], "Glyph ids cannot depend on size.");
            Assert.AreEqual(small.Positions[index].X * 2f, large.Positions[index].X, 0.001f);
        }
    }

    [TestMethod]
    public void GeometryIsByteIdenticalAcrossIndependentRuns()
    {
        // Appendix C.3. Two typesetters built from scratch, sharing nothing but the pinned font
        // bytes and the pinned HarfBuzz, must produce the same arrays — otherwise a scene's
        // rendered output would depend on how many times the process had run it, and §2.2's
        // per-environment reproducibility would be a claim rather than a fact.
        using var first = new Typesetter();
        using var second = new Typesetter();
        var request = new TypesetRequest("Najm — figure 3\nsecond line", Style())
        {
            Align = TextAlign.Center,
            LineSpacing = 1.25f,
        };

        var a = first.Typeset(request);
        var b = second.Typeset(request);

        Assert.AreNotSame(a, b, "Two typesetters must not share a cache; this test would be vacuous.");
        CollectionAssert.AreEqual(a.Glyphs.ToArray(), b.Glyphs.ToArray());
        CollectionAssert.AreEqual(a.Clusters.ToArray(), b.Clusters.ToArray());
        CollectionAssert.AreEqual(a.Positions.ToArray(), b.Positions.ToArray());
        Assert.AreEqual(a.LogicalBounds, b.LogicalBounds);
        Assert.AreEqual(a.InkBounds, b.InkBounds);
    }

    [TestMethod]
    public void ConstraintsThatChangeGeometryAreCacheKeysAndOnesThatDoNotAreNot()
    {
        using var typesetter = new Typesetter();

        typesetter.Typeset(new TypesetRequest("one\ntwo", Style()) { Align = TextAlign.Left });
        Assert.AreEqual(1, typesetter.CachedLayoutCount);

        typesetter.Typeset(new TypesetRequest("one\ntwo", Style()) { Align = TextAlign.Center });
        Assert.AreEqual(2, typesetter.CachedLayoutCount, "Alignment moves glyphs, so it is a key.");

        typesetter.Typeset(new TypesetRequest("one\ntwo", Style()) { LineSpacing = 2f });
        Assert.AreEqual(3, typesetter.CachedLayoutCount, "Leading moves baselines, so it is a key.");

        typesetter.Typeset(new TypesetRequest("one\ntwo", Style()) { Align = TextAlign.Left });
        Assert.AreEqual(3, typesetter.CachedLayoutCount, "A repeat of the first request adds nothing.");
    }

    [TestMethod]
    public void FeaturesAreAShapingKeyAndSplitTheShapedRunCache()
    {
        using var typesetter = new Typesetter();

        typesetter.Typeset(new TypesetRequest("fi", Style()));
        typesetter.Typeset(new TypesetRequest("fi", new Style
        {
            Size = Size,
            Features = new FontFeatures { Ligatures = false },
        }));

        // Two runs of the same characters in the same face with different features are different
        // glyphs, not the same glyphs drawn differently — so they must not share a shaped entry.
        Assert.AreEqual(2, typesetter.CachedShapedRunCount);
    }

    [TestMethod]
    public void ARepeatedLineSharesOneShapedRunAcrossLayouts()
    {
        using var typesetter = new Typesetter();

        var layout = typesetter.Typeset(new TypesetRequest("same\nsame\nsame", Style()));

        // Shaping is keyed by the line's characters, so three identical lines shape once. The
        // layout still positions them at three baselines, which is the split the cache design is
        // built on: shaping is content, layout is geometry.
        Assert.AreEqual(3, layout.LineCount);
        Assert.AreEqual(1, typesetter.CachedShapedRunCount);
    }

    private static Style Style() => new() { Size = Size };
}
