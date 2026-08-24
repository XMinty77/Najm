using Najm.Core;
using Najm.Core.Text;

namespace Najm.Lib.Tests.Text;

/// <summary>
/// NAJM-TEXT I.9's upright rule, in the frame where it is decided: the node's own local space.
/// </summary>
/// <remarks>
/// <para>
/// Fonts and Skia are Y-down; <see cref="WorldLayer2D"/> is Y-up with the flip living in the camera.
/// Drawing a layout straight into a world layer would render every glyph mirrored, because the
/// camera's flip is still to come. The pin is that the <em>node</em> composes the reading frame into
/// local space, so in a Y-up layer that composition is itself a flip and the camera's cancels it.
/// </para>
/// <para>
/// These tests read the consequence rather than the mechanism, because the consequence is what an
/// author sees and what a sample depends on: <strong>in a world layer ascenders extend toward +y and
/// line 2 stacks toward −y; in a screen layer, the reverse.</strong> The pixel half of the proof —
/// that a glyph actually reads upright in both — lives with the backend that rasterizes it, in
/// <c>Najm.Skia.Tests</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class TextNodeUprightRuleTests
{
    private const float Size = 64f;

    [TestMethod]
    public void AWorldLayerPutsAscendersTowardPositiveY()
    {
        using var scene = TextTestScene.World();
        var node = scene.Add(new TextNode("Hxg") { Size = Size });
        var metrics = scene.Typesetter.Metrics(node.Layout.Runs[0].Font, Size);

        var bounds = node.GeometryBounds;

        // The whole sign test, in two lines. Anchored on its baseline in a Y-up layer, the box runs
        // from one descent below the origin to one ascent above it — so a label placed "above" a
        // data point really is point + (0, h), which is the maths convention the plot code assumes.
        Assert.AreEqual(-metrics.Descent, bounds.Top, 0.001f, "The box's low edge is one descent below the baseline.");
        Assert.AreEqual(metrics.Ascent, bounds.Bottom, 0.001f, "The box's high edge is one ascent above it.");
        Assert.IsTrue(node.YAxisPointsUp, "A world layer's y-axis points up, and the node must know it.");
    }

    [TestMethod]
    public void AScreenLayerPutsAscendersTowardNegativeY()
    {
        using var scene = TextTestScene.Screen();
        var node = scene.Add(new TextNode("Hxg") { Size = Size });
        var metrics = scene.Typesetter.Metrics(node.Layout.Runs[0].Font, Size);

        var bounds = node.GeometryBounds;

        // Exactly the mirror of the world case, and the reading frame unmodified: a screen layer is
        // already Y-down, so the node composes an identity and the layout's own box is the answer.
        Assert.AreEqual(-metrics.Ascent, bounds.Top, 0.001f);
        Assert.AreEqual(metrics.Descent, bounds.Bottom, 0.001f);
        Assert.IsFalse(node.YAxisPointsUp);
    }

    [TestMethod]
    public void LineTwoStacksTowardNegativeYInAWorldLayer()
    {
        using var scene = TextTestScene.World();
        var node = scene.Add(new TextNode("first\nsecond") { Size = Size });
        var metrics = scene.Typesetter.Metrics(node.Layout.Runs[0].Font, Size);

        var second = node.Baseline(1);

        // Reading order runs down the page whichever way the layer's y points, so in a Y-up layer
        // the second line is one line height *below* the first in local coordinates. Getting this
        // backwards would stack a two-line caption upward, which reads as reversed lines rather than
        // as mirrored glyphs and is therefore the harder of the two mistakes to notice.
        Assert.AreEqual(0f, node.Baseline(0).Y, 0f);
        Assert.AreEqual(-metrics.LineHeight, second.Y, 0.001f);
        Assert.AreEqual(
            -(metrics.LineHeight + metrics.Descent),
            node.GeometryBounds.Top,
            0.001f,
            "The box must reach one descent past the last baseline.");
    }

    [TestMethod]
    public void LineTwoStacksTowardPositiveYInAScreenLayer()
    {
        using var scene = TextTestScene.Screen();
        var node = scene.Add(new TextNode("first\nsecond") { Size = Size });
        var metrics = scene.Typesetter.Metrics(node.Layout.Runs[0].Font, Size);

        Assert.AreEqual(0f, node.Baseline(0).Y, 0f);
        Assert.AreEqual(metrics.LineHeight, node.Baseline(1).Y, 0.001f);
        Assert.AreEqual(metrics.LineHeight + metrics.Descent, node.GeometryBounds.Bottom, 0.001f);
    }

    [TestMethod]
    public void TheTwoLayerKindsProduceExactlyMirroredBounds()
    {
        using var screenScene = TextTestScene.Screen();
        using var worldScene = TextTestScene.World();
        var screen = screenScene.Add(new TextNode("Najm\ntext") { Size = Size, Anchor = TextAnchor.Center });
        var world = worldScene.Add(new TextNode("Najm\ntext") { Size = Size, Anchor = TextAnchor.Center });

        var a = screen.GeometryBounds;
        var b = world.GeometryBounds;

        // Same layout, same anchor, opposite visual up: x is untouched and y is negated about the
        // anchor point. Under a centre anchor the box is symmetric in y, so the two coincide — which
        // is the cleanest statement that the difference is a reflection and nothing else.
        Assert.AreEqual(a.Left, b.Left, 0.001f);
        Assert.AreEqual(a.Width, b.Width, 0.001f);
        Assert.AreEqual(a.Height, b.Height, 0.001f);
        Assert.AreEqual(-a.Bottom, b.Top, 0.001f);
        Assert.AreEqual(-a.Top, b.Bottom, 0.001f);
    }

    [TestMethod]
    public void HitBoundsLiveInTheSameCorrectedFrameAsGeometry()
    {
        using var scene = TextTestScene.World();
        var node = scene.Add(new TextNode("Hxg") { Size = Size });

        // I.9 says hit boxes live in the corrected frame, and §6.6's default is that the hit gate
        // follows geometry. A node that corrected one and not the other would draw text in one place
        // and answer clicks in its mirror image.
        Assert.AreEqual(node.GeometryBounds, node.HitBounds);
    }

    [TestMethod]
    public void VisualBoundsFollowInkAndAreCorrectedToo()
    {
        using var scene = TextTestScene.World();
        var node = scene.Add(new TextNode("H") { Size = Size });

        var visual = node.VisualBounds;
        var geometry = node.GeometryBounds;

        // 'H' has no descender, so its ink sits entirely above the baseline in a Y-up layer: the
        // visual box starts at y = 0 and reaches the cap height, strictly inside the metric box.
        Assert.AreEqual(0f, visual.Top, 0.001f, "'H' has no descender, so no ink falls below the baseline.");
        Assert.IsLessThan(geometry.Bottom, visual.Bottom, "Cap height is below the face's ascent.");
        Assert.IsGreaterThan(geometry.Top, visual.Top, "The metric box reaches below the ink by one descent.");
    }

    [TestMethod]
    public void TheLayerFactIsReadAtAttachAndNotBefore()
    {
        var node = new TextNode("Najm") { Size = Size };
        Assert.IsFalse(node.YAxisPointsUp, "A detached node has no layer to read, so it reports the reading frame.");

        using var scene = TextTestScene.World();
        scene.Add(node);

        // I.9: the node reads Layer.YAxisPointsUp at attach. It is a layer fact rather than a camera
        // fact, which is what keeps bounds camera-free by construction (§6.6) — nothing about
        // panning, zooming, or swapping cameras can reach it.
        Assert.IsTrue(node.YAxisPointsUp);
    }
}
