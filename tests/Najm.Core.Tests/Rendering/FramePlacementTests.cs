using System.Numerics;

namespace Najm.Core.Tests.Rendering;

/// <summary>
/// The one rule for landing a virtual frame on an output surface: fit, never stretch, and centre
/// what fits.
/// </summary>
[TestClass]
public sealed class FramePlacementTests
{
    [TestMethod]
    public void TheRenderScaleIsTheLargestUniformScaleThatFits()
    {
        // A matched aspect scales on both axes at once.
        Assert.AreEqual(2f, FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(16, 8)));

        // A too-tall target is limited by width, a too-wide one by height.
        Assert.AreEqual(1f, FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(8, 8)));
        Assert.AreEqual(1f, FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(16, 4)));

        // 1920×1080 into 1920×1200 is still 1: the extra 120 rows become bars, not stretch.
        Assert.AreEqual(1f, FramePlacement.ResolveRenderScale(new Vector2(1920f, 1080f), new PixelSize(1920, 1200)));
    }

    [TestMethod]
    public void TheRenderScaleRejectsAnUnusablePair()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveRenderScale(new Vector2(0f, 4f), new PixelSize(8, 8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveRenderScale(new Vector2(float.NaN, 4f), new PixelSize(8, 8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), default));
    }

    [TestMethod]
    public void TheContentOffsetCentresAndSendsTheOddPixelToTheFarBar()
    {
        // Even leftovers split exactly.
        Assert.AreEqual(0, FramePlacement.ResolveContentOffset(8, 8));
        Assert.AreEqual(2, FramePlacement.ResolveContentOffset(8, 4));
        Assert.AreEqual(4, FramePlacement.ResolveContentOffset(16, 8));

        // An odd leftover of 5 becomes 2 before and 3 after: floor((9 − 4) / 2) = 2.
        Assert.AreEqual(2, FramePlacement.ResolveContentOffset(9, 4));
        Assert.AreEqual(3, FramePlacement.ResolveContentOffset(15, 8));
        Assert.AreEqual(0, FramePlacement.ResolveContentOffset(3, 2));

        // Never negative: the outward-rounded content can exceed the output by a pixel on the
        // limiting axis, and pushing content off the near edge to save one at the far edge is worse
        // than losing the far pixel to the surface bound.
        Assert.AreEqual(0, FramePlacement.ResolveContentOffset(8, 9));
        Assert.AreEqual(0, FramePlacement.ResolveContentOffset(8, 100));
    }

    [TestMethod]
    public void AMatchedAspectFillsTheOutputWithNoBars()
    {
        var scale = FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(16, 8));

        Assert.AreEqual(
            new Rect(0f, 0f, 16f, 8f),
            FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(16, 8), scale));
    }

    [TestMethod]
    public void ATooTallOutputCarriesEqualBarsAboveAndBelow()
    {
        // 8×4 at scale 1 is 8×4 pixels of content in an 8×8 target: 4 leftover rows, 2 each side,
        // so the content occupies rows 2 through 5.
        var scale = FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(8, 8));

        Assert.AreEqual(1f, scale);
        Assert.AreEqual(
            new Rect(0f, 2f, 8f, 4f),
            FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(8, 8), scale));
    }

    [TestMethod]
    public void ATooWideOutputCarriesEqualBarsLeftAndRight()
    {
        // 8 leftover columns in a 16-wide target, 4 each side, so content occupies columns 4..11.
        var scale = FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(16, 4));

        Assert.AreEqual(1f, scale);
        Assert.AreEqual(
            new Rect(4f, 0f, 8f, 4f),
            FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(16, 4), scale));
    }

    [TestMethod]
    public void AnOddLeftoverGivesTheExtraPixelToTheRightAndBottom()
    {
        // Height: 9 − 4 = 5 leftover rows, so 2 above and 3 below.
        var tall = FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(8, 9));
        Assert.AreEqual(1f, tall);
        Assert.AreEqual(
            new Rect(0f, 2f, 8f, 4f),
            FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(8, 9), tall));

        // Width: 15 − 8 = 7 leftover columns, so 3 left and 4 right.
        var wide = FramePlacement.ResolveRenderScale(new Vector2(8f, 4f), new PixelSize(15, 4));
        Assert.AreEqual(1f, wide);
        Assert.AreEqual(
            new Rect(3f, 0f, 8f, 4f),
            FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(15, 4), wide));
    }

    [TestMethod]
    public void AFractionalContentExtentRoundsOutwardBeforeItIsCentred()
    {
        // 3 virtual units at scale 0.5 is 1.5 device pixels, covered by 2. In a 9-pixel output that
        // leaves 7, so the offset is 3 and the far bar keeps the odd pixel.
        var rect = FramePlacement.ResolveContentRect(new Vector2(3f, 3f), new PixelSize(9, 9), 0.5f);

        Assert.AreEqual(new Rect(3f, 3f, 2f, 2f), rect);
    }

    [TestMethod]
    public void TheContentRectRejectsAnUnusableArgument()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveContentRect(new Vector2(8f, 0f), new PixelSize(8, 8), 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveContentRect(new Vector2(8f, 4f), default, 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(8, 8), 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => FramePlacement.ResolveContentRect(new Vector2(8f, 4f), new PixelSize(8, 8), float.PositiveInfinity));
    }
}
