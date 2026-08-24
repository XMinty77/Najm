using System.Numerics;
using Najm.Core;
using Najm.Core.Text;

namespace Najm.Lib.Tests.Text;

/// <summary>NAJM-TEXT I.9: all twelve anchors against a golden box, and baseline anchors baseline-true.</summary>
/// <remarks>
/// The golden box is the layout's own <see cref="ITextLayout.LogicalBounds"/>, read back from the
/// same layout the node is showing. That is the point of the test: an anchor is a pure offset over
/// a finished layout, so every expectation below is that box translated by a named corner of itself
/// — arithmetic, with no font number in it anywhere.
/// </remarks>
[TestClass]
public sealed class TextNodeAnchorTests
{
    [TestMethod]
    public void EveryAnchorPutsItsOwnPointOfTheLogicalBoxAtTheOrigin()
    {
        using var world = TextTestScene.Screen();
        var node = world.Add(new TextNode("Najm") { Size = 64f });
        var box = node.Layout.LogicalBounds;
        var w = box.Width;
        var h = box.Height;

        // The full table, stated once. Each row is "the box, shifted so that the named point lands
        // on (0, 0)" — which is exactly what an anchor means and exactly what the node must do.
        var expected = new Dictionary<TextAnchor, Rect>
        {
            [TextAnchor.BaselineLeft] = new(0f, box.Top, w, h),
            [TextAnchor.BaselineCenter] = new(-w / 2f, box.Top, w, h),
            [TextAnchor.BaselineRight] = new(-w, box.Top, w, h),
            [TextAnchor.TopLeft] = new(0f, 0f, w, h),
            [TextAnchor.TopCenter] = new(-w / 2f, 0f, w, h),
            [TextAnchor.TopRight] = new(-w, 0f, w, h),
            [TextAnchor.CenterLeft] = new(0f, -h / 2f, w, h),
            [TextAnchor.Center] = new(-w / 2f, -h / 2f, w, h),
            [TextAnchor.CenterRight] = new(-w, -h / 2f, w, h),
            [TextAnchor.BottomLeft] = new(0f, -h, w, h),
            [TextAnchor.BottomCenter] = new(-w / 2f, -h, w, h),
            [TextAnchor.BottomRight] = new(-w, -h, w, h),
        };

        Assert.HasCount(12, expected, "I.9 defines twelve anchors; the table must cover all of them.");
        Assert.HasCount(12, Enum.GetValues<TextAnchor>());

        foreach (var (anchor, want) in expected)
        {
            node.Anchor = anchor;
            var got = node.GeometryBounds;
            Assert.AreEqual(want.Left, got.Left, 0.001f, $"{anchor} left");
            Assert.AreEqual(want.Top, got.Top, 0.001f, $"{anchor} top");
            Assert.AreEqual(want.Width, got.Width, 0.001f, $"{anchor} width");
            Assert.AreEqual(want.Height, got.Height, 0.001f, $"{anchor} height");
        }
    }

    [TestMethod]
    public void TheThreeBaselineAnchorsPutTheFirstBaselineOnTheOrigin()
    {
        using var scene = TextTestScene.Screen();
        var node = scene.Add(new TextNode("2.0\nsecond") { Size = 64f });

        foreach (var anchor in new[] { TextAnchor.BaselineLeft, TextAnchor.BaselineCenter, TextAnchor.BaselineRight })
        {
            node.Anchor = anchor;

            // Baseline-true means exactly this: y = 0 is the first line's baseline, not a box edge.
            // A tick label anchored this way sits on its tick with no ascent arithmetic at the call
            // site, which is why it is the default.
            Assert.AreEqual(0f, node.Baseline(0).Y, 0f, $"{anchor} must be baseline-true.");
        }

        node.Anchor = TextAnchor.TopLeft;
        Assert.AreNotEqual(
            0f,
            node.Baseline(0).Y,
            "A box anchor references the logical box, so its baseline is not at the origin.");
    }

    [TestMethod]
    public void MixedSizeLabelsShareABaselineWithoutAscentArithmetic()
    {
        using var scene = TextTestScene.Screen();
        var small = scene.Add(new TextNode("x") { Size = 24f, Anchor = TextAnchor.BaselineCenter });
        var large = scene.Add(new TextNode("X") { Size = 96f, Anchor = TextAnchor.BaselineCenter });

        // The reason the default is a baseline anchor rather than a box anchor: two labels at
        // different sizes placed at the same y line up along the baseline they share. Under a top
        // anchor they would line up along their ascents and read as staggered.
        Assert.AreEqual(0f, small.Baseline(0).Y, 0f);
        Assert.AreEqual(0f, large.Baseline(0).Y, 0f);
        Assert.AreNotEqual(
            small.GeometryBounds.Top,
            large.GeometryBounds.Top,
            "The two boxes differ in height; only their baselines coincide.");
    }

    [TestMethod]
    public void ChangingTheAnchorMovesTheTextWithoutTypesettingAnything()
    {
        using var counter = CountingTypesetter.Real();
        using var scene = TextTestScene.Screen(counter);
        var node = scene.Add(new TextNode("Najm") { Size = 64f });

        var atBaseline = node.GeometryBounds;
        var typesetsAfterFirstRead = counter.TypesetCount;
        var layoutsAfterFirstRead = counter.CachedLayoutCount;

        foreach (var anchor in Enum.GetValues<TextAnchor>())
        {
            node.Anchor = anchor;
            _ = node.GeometryBounds;
        }

        // I.3's cache-purity reason for keeping the anchor out of the request, measured: twelve
        // anchors over identical geometry are one cache entry, not twelve, and cost no work at all.
        Assert.AreEqual(typesetsAfterFirstRead, counter.TypesetCount, "Anchoring must not re-typeset.");
        Assert.AreEqual(layoutsAfterFirstRead, counter.CachedLayoutCount, "Anchoring must not add a cache entry.");

        node.Anchor = TextAnchor.BaselineLeft;
        Assert.AreEqual(atBaseline, node.GeometryBounds, "Returning to an anchor must return the geometry.");
    }

    [TestMethod]
    public void ReadingToLocalIsTheTransformTheBoundsWereBuiltFrom()
    {
        using var scene = TextTestScene.Screen();
        var node = scene.Add(new TextNode("Najm") { Size = 64f, Anchor = TextAnchor.Center });
        var box = node.Layout.LogicalBounds;

        // The transform is public because a caller working with layout positions directly — baking
        // outlines for a clip, say — needs the same mapping the node used. It must therefore be the
        // one the node used, not a second one that agrees by accident.
        var topLeft = Vector2.Transform(new Vector2(box.Left, box.Top), node.ReadingToLocal);
        var bottomRight = Vector2.Transform(new Vector2(box.Right, box.Bottom), node.ReadingToLocal);

        Assert.AreEqual(node.GeometryBounds.Left, MathF.Min(topLeft.X, bottomRight.X), 0.001f);
        Assert.AreEqual(node.GeometryBounds.Top, MathF.Min(topLeft.Y, bottomRight.Y), 0.001f);
    }
}
