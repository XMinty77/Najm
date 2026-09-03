using System.Numerics;
using Najm.Core;

namespace Najm.Host.Desktop.Tests;

/// <summary>
/// Pins the arithmetic of ARCHITECTURE §5.1's single scaling point, in both directions.
/// </summary>
/// <remarks>
/// The interesting cases are all the ones where the output aspect differs from the scene's, because
/// that is when the content rectangle stops starting at the origin and every mapping that forgot
/// the bars starts producing plausible-looking wrong answers.
/// </remarks>
[TestClass]
public sealed class LetterboxTests
{
    private static readonly Vector2 Widescreen = new(1920f, 1080f);

    [TestMethod]
    public void AWiderOutputPillarboxes_AndAnEqualAspectDoesNot()
    {
        // 16:9 into 2:1 — the frame fits on height and leaves bars left and right.
        var wide = Letterbox.Resolve(Widescreen, new PixelSize(1440, 720));
        Assert.AreEqual(720f / 1080f, wide.RenderScale);
        Assert.AreEqual(new Rect(80f, 0f, 1280f, 720f), wide.ContentRect);
        Assert.IsTrue(wide.HasBars);

        // 16:9 into 16:9 — no bars, and the content rect is the whole output.
        var exact = Letterbox.Resolve(Widescreen, new PixelSize(1280, 720));
        Assert.AreEqual(new Rect(0f, 0f, 1280f, 720f), exact.ContentRect);
        Assert.IsFalse(exact.HasBars);

        // 16:9 into 4:3 — the frame fits on width and leaves bars above and below.
        var tall = Letterbox.Resolve(Widescreen, new PixelSize(1024, 768));
        Assert.AreEqual(new Rect(0f, 96f, 1024f, 576f), tall.ContentRect);
        Assert.IsTrue(tall.HasBars);
    }

    [TestMethod]
    public void TheContentRectIsTheOneFramePlacementResolves()
    {
        // Not an independent implementation: the compositor places the frame through these two
        // calls, so a divergence here would be a divergence between the picture and the clicks.
        var outputSize = new PixelSize(1000, 700);
        var box = Letterbox.Resolve(Widescreen, outputSize);

        Assert.AreEqual(FramePlacement.ResolveRenderScale(Widescreen, outputSize), box.RenderScale);
        Assert.AreEqual(
            FramePlacement.ResolveContentRect(Widescreen, outputSize, box.RenderScale),
            box.ContentRect);
    }

    [TestMethod]
    public void APointInTheOutputMapsToTheVirtualPointUnderIt()
    {
        var box = Letterbox.Resolve(Widescreen, new PixelSize(1440, 720));

        // The content rect's own corners are the virtual frame's corners.
        Assert.AreEqual(Vector2.Zero, box.ToVirtual(new Vector2(80f, 0f)));
        Assert.AreEqual(Widescreen, box.ToVirtual(new Vector2(1360f, 720f)));

        // And its centre is the frame's centre — the assertion a host that ignored the 80-pixel
        // bar would fail while still looking right on a 16:9 window.
        Assert.AreEqual(new Vector2(960f, 540f), box.ToVirtual(new Vector2(720f, 360f)));
    }

    [TestMethod]
    public void PointsOutsideTheContentRectMapUnclamped()
    {
        // §9.1: "Pointer coordinates outside the letterbox map linearly and are delivered
        // unclamped". A drag that leaves the window keeps producing usable deltas because of this.
        var box = Letterbox.Resolve(Widescreen, new PixelSize(1440, 720));

        var left = box.ToVirtual(new Vector2(0f, 0f));
        Assert.AreEqual(-120f, left.X, 1e-3f, "80 device pixels of bar at a scale of 2/3.");
        Assert.AreEqual(0f, left.Y);

        var beyond = box.ToVirtual(new Vector2(1440f, 900f));
        Assert.IsGreaterThan(Widescreen.X, beyond.X);
        Assert.IsGreaterThan(Widescreen.Y, beyond.Y);
    }

    [TestMethod]
    public void TheTwoDirectionsAreInverses()
    {
        foreach (var outputSize in new[]
                 {
                     new PixelSize(1440, 720),
                     new PixelSize(1024, 768),
                     new PixelSize(1280, 720),
                     new PixelSize(333, 999),
                 })
        {
            var box = Letterbox.Resolve(Widescreen, outputSize);
            foreach (var point in new[]
                     {
                         Vector2.Zero,
                         new Vector2(960f, 540f),
                         new Vector2(1920f, 1080f),
                         new Vector2(-200f, 2000f),
                     })
            {
                var roundTripped = box.ToVirtual(box.ToOutput(point));
                Assert.AreEqual(point.X, roundTripped.X, 1e-2f, $"{outputSize} / {point}");
                Assert.AreEqual(point.Y, roundTripped.Y, 1e-2f, $"{outputSize} / {point}");
            }
        }
    }

    [TestMethod]
    public void TheBarsAreTheOutputMinusTheContentRect()
    {
        Span<Rect> bars = stackalloc Rect[2];

        var wide = Letterbox.Resolve(Widescreen, new PixelSize(1440, 720));
        Assert.AreEqual(2, wide.GetBars(bars));
        Assert.AreEqual(new Rect(0f, 0f, 80f, 720f), bars[0]);
        Assert.AreEqual(new Rect(1360f, 0f, 80f, 720f), bars[1]);

        var tall = Letterbox.Resolve(Widescreen, new PixelSize(1024, 768));
        Assert.AreEqual(2, tall.GetBars(bars));
        Assert.AreEqual(new Rect(0f, 0f, 1024f, 96f), bars[0]);
        Assert.AreEqual(new Rect(0f, 672f, 1024f, 96f), bars[1]);

        Assert.AreEqual(0, Letterbox.Resolve(Widescreen, new PixelSize(1280, 720)).GetBars(bars));
    }

    [TestMethod]
    public void AnOddLeftoverGivesTheExtraPixelToTheFarBar()
    {
        // FramePlacement's rounding rule, seen from the bars: floor((output - content) / 2) before,
        // the rest after. A host that split it the other way would shift the picture by a pixel
        // relative to where the compositor puts it, and every click would be a pixel out.
        Span<Rect> bars = stackalloc Rect[2];
        var box = Letterbox.Resolve(new Vector2(4f, 4f), new PixelSize(9, 4));

        Assert.AreEqual(new Rect(2f, 0f, 4f, 4f), box.ContentRect);
        Assert.AreEqual(2, box.GetBars(bars));
        Assert.AreEqual(2f, bars[0].Width);
        Assert.AreEqual(3f, bars[1].Width);
    }

    [TestMethod]
    public void GetBarsRefusesASpanThatCannotHoldBoth()
    {
        var box = Letterbox.Resolve(Widescreen, new PixelSize(1440, 720));
        Assert.ThrowsExactly<ArgumentException>(() => ThrowingGetBars(box));

        static void ThrowingGetBars(Letterbox box)
        {
            Span<Rect> single = stackalloc Rect[1];
            box.GetBars(single);
        }
    }

    [TestMethod]
    public void ResolveRefusesGeometryThatHasNoPlacement()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Letterbox.Resolve(new Vector2(0f, 1080f), new PixelSize(1280, 720)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Letterbox.Resolve(Widescreen, default));
    }
}
