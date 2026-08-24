using Najm.Core;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Lib.Tests.Text;

/// <summary>NAJM-TEXT I.10: when a text node typesets, and — mostly — when it does not.</summary>
[TestClass]
public sealed class TextNodeLazinessTests
{
    [TestMethod]
    public void AFivePropertySetupCostsExactlyOneTypeset()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode());

        node.Text = "Najm";
        node.Size = 48f;
        node.Family = Najm.Text.Typesetter.LatinModernRoman;
        node.Align = TextAlign.Center;
        node.LineSpacing = 1.25f;

        // Nothing has been read yet, so nothing has been built. §4.1's benign memoization: a
        // property write is a comparison, a field store, and a flag — the work happens at the first
        // read, once, for whatever the properties finally say.
        Assert.AreEqual(0, counter.TypesetCount, "A property write must not typeset.");

        _ = node.GeometryBounds;
        Assert.AreEqual(1, counter.TypesetCount, "The first read builds the layout, once.");

        _ = node.GeometryBounds;
        _ = node.VisualBounds;
        _ = node.Layout;
        _ = node.Baseline(0);
        Assert.AreEqual(1, counter.TypesetCount, "Later reads must return the memoized layout.");
    }

    [TestMethod]
    public void WritingTheSameValueBackIsNotAChange()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode("Najm") { Size = 48f });
        _ = node.GeometryBounds;
        var built = counter.TypesetCount;

        node.Text = "Najm";
        node.Size = 48f;
        _ = node.GeometryBounds;

        // A per-frame binding that assigns the same string every tick is the common case, not an
        // exotic one. Comparing before storing is what keeps it free.
        Assert.AreEqual(built, counter.TypesetCount);
    }

    [TestMethod]
    public void ChangingTheColorNeverRetypesetsAndNeverAddsACacheEntry()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode("Najm") { Size = 48f });
        _ = node.GeometryBounds;
        var typesets = counter.TypesetCount;
        var entries = counter.CachedLayoutCount;

        for (var step = 0; step <= 100; step++)
        {
            node.Color = Color.Srgb(step / 100f, 0f, 0f);
            _ = node.GeometryBounds;
        }

        // I.4: uniform node colour is a draw-time override, not part of the request. A hundred-frame
        // colour tween therefore leaves the layout cache exactly as it found it — which is the whole
        // reason hover highlighting costs nothing.
        Assert.AreEqual(typesets, counter.TypesetCount);
        Assert.AreEqual(entries, counter.CachedLayoutCount);
    }

    [TestMethod]
    public void ChangingTheSizeReTypesetsAndSaysSoThroughTheCacheCount()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode("Najm") { Size = 48f });
        _ = node.GeometryBounds;

        node.Size = 49f;
        _ = node.GeometryBounds;

        // The documented anti-idiom, made visible rather than forbidden. Size is a typesetting input
        // and a cache key, so a size tween adds an entry per frame; Transform.Scale is the idiom
        // that does not. It still works, which is the point — it is a cost, not an error.
        Assert.AreEqual(2, counter.TypesetCount);
        Assert.AreEqual(2, counter.CachedLayoutCount);
    }

    [TestMethod]
    public void TwoNodesShowingTheSameThingShareOneLayout()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var first = scene.Add(new TextNode("2.0") { Size = 48f, Anchor = TextAnchor.BaselineCenter });
        var second = scene.Add(new TextNode("2.0") { Size = 48f, Anchor = TextAnchor.TopRight });

        // Appendix B.1's tick labels: distinct strings become distinct entries and duplicates share
        // handles, anchors notwithstanding. Both nodes asked, so the typesetter was called twice —
        // and answered with one object.
        Assert.AreSame(first.Layout, second.Layout);
        Assert.AreEqual(2, counter.TypesetCount);
        Assert.AreEqual(1, counter.CachedLayoutCount);
    }

    [TestMethod]
    public void ADetachedNodeSaysWhyItCannotMeasure()
    {
        var node = new TextNode("Najm");

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => _ = node.GeometryBounds);

        // A node with no scene has no typesetter, and returning an empty box instead would put a
        // label of size zero into an arrangement helper and let the author discover it as a
        // mysterious overlap much later.
        Assert.Contains("loaded scene", error.Message);
        Assert.Contains("NullTypesetter", error.Message);
        Assert.Contains("Najm.Text.Typesetter", error.Message);
    }

    [TestMethod]
    public void MaxWidthIsRefusedAtThePropertySet()
    {
        var node = new TextNode("wrap me");

        var error = Assert.ThrowsExactly<NotSupportedException>(() => node.MaxWidth = 400f);

        // VI.3's "fail at property set" applied as early as it can be: the exception carries the
        // author's own line number, not a stack from inside the typesetter three frames later.
        Assert.Contains("MaxWidth", error.Message);
        Assert.Contains("hard newlines", error.Message);
        node.MaxWidth = null;
    }

    [TestMethod]
    public void ANodeWithNoTextCoversNoAreaButStillHasALineToMeasureAgainst()
    {
        using var scene = TextTestScene.Screen();
        var metrics = scene.Typesetter.Metrics(
            scene.Add(new TextNode("x") { Size = 48f }).Layout.Runs[0].Font,
            48f);
        var node = scene.Add(new TextNode { Size = 48f });

        Assert.IsEmpty(node.Layout.Runs.ToArray(), "There is nothing to draw.");

        // The node's bounds collapse to default, which is the engine's own convention rather than
        // this node's opinion: Node2D normalizes any rectangle covering no area to default so that
        // an empty contribution carries no position an ancestor could mistake for one. A zero-width
        // box is exactly that, whatever its nominal height.
        Assert.IsTrue(node.GeometryBounds.IsEmpty);
        Assert.AreEqual(default, node.GeometryBounds);

        // The line itself does not disappear, though, and that is what a caret or an underline
        // needs: the layout still reports one line at the face's height, and the baseline is still
        // at the origin, so a label that has just been cleared does not make its neighbours jump.
        Assert.AreEqual(1, node.Layout.LineCount);
        Assert.AreEqual(metrics.Ascent + metrics.Descent, node.Layout.LogicalBounds.Height, 0.001f);
        Assert.AreEqual(0f, node.Baseline(0).Y, 0f);
    }

    [TestMethod]
    public void DetachingReleasesTheLayoutAndReattachingRebuildsIt()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode("Najm") { Size = 48f });
        _ = node.GeometryBounds;

        scene.Root.Remove(node);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = node.GeometryBounds);

        scene.Root.Add(node);
        _ = node.GeometryBounds;

        // The node re-resolves its capability and its layer's orientation on every attach, because
        // it may be attaching somewhere else entirely. The layout it rebuilds is the cached one, so
        // the second typeset is a dictionary probe rather than a reshape.
        Assert.AreEqual(2, counter.TypesetCount);
        Assert.AreEqual(1, counter.CachedLayoutCount);
    }
}
