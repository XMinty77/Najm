namespace Najm.Core.Tests.SceneGraph;

/// <summary>
/// The author-facing surface of §6.7's composition algebra, restricted to the properties M1
/// implements. The tests that matter most here are the two negative ones: that the defaults are the
/// values which cost nothing, and that the M2 properties are absent rather than present and ignored.
/// </summary>
[TestClass]
public sealed class NodeCompositionTests
{
    [TestMethod]
    public void CompositionPropertiesDefaultToTheValuesThatRequireNoIsolation()
    {
        var node = new Node2D();

        Assert.AreEqual(1f, node.Opacity);
        Assert.AreEqual(BlendMode.SrcOver, node.Blend);
        Assert.IsNull(node.Clip);
        Assert.IsFalse(node.Isolate);
    }

    [TestMethod]
    public void OpacityAcceptsTheClosedUnitIntervalAndRejectsEverythingElse()
    {
        var node = new Node2D();

        foreach (var accepted in new[] { 0f, 0.25f, 0.5f, 1f })
        {
            node.Opacity = accepted;
            Assert.AreEqual(accepted, node.Opacity);
        }

        foreach (var rejected in new[]
                 {
                     -0.000001f,
                     1.000001f,
                     float.NaN,
                     float.PositiveInfinity,
                     float.NegativeInfinity,
                 })
        {
            var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => node.Opacity = rejected);
            Assert.AreEqual("value", failure.ParamName);
        }

        // The rejected assignments left the last accepted value standing.
        Assert.AreEqual(1f, node.Opacity);
    }

    [TestMethod]
    public void BlendAcceptsEveryDefinedModeAndRejectsAnUndefinedOne()
    {
        var node = new Node2D();

        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            node.Blend = mode;
            Assert.AreEqual(mode, node.Blend);
        }

        node.Blend = BlendMode.SrcOver;
        var failure = Assert.ThrowsExactly<ArgumentException>(() => node.Blend = (BlendMode)9999);
        Assert.AreEqual("value", failure.ParamName);
        Assert.AreEqual(BlendMode.SrcOver, node.Blend);
    }

    [TestMethod]
    public void ClipTakesAnyRectangleAndClearsBackToNull()
    {
        // Rect validates its own components at construction, so a Rect? is either null or already
        // finite and nonnegative — there is nothing left for this setter to reject, and inventing a
        // second guard would only be able to disagree with the first.
        var node = new Node2D();

        node.Clip = new Rect(-10f, -4f, 20f, 8f);
        Assert.AreEqual(new Rect(-10f, -4f, 20f, 8f), node.Clip);

        // The empty rectangle is a legal thing to say and means "paint nothing", which is different
        // from "clip nothing".
        node.Clip = default(Rect);
        Assert.AreEqual(default(Rect), node.Clip);

        node.Clip = null;
        Assert.IsNull(node.Clip);
    }

    [TestMethod]
    public void TheMilestoneTwoCompositionPropertiesAreAbsentRatherThanApproximated()
    {
        // §6.7 lists eight properties; M1 implements four of them plus ZIndex, which predates this
        // work. Clip joined the list once the unit bracket could carry it — the rectangular case
        // only. Mask, Effect, and Backdrop each still need machinery that does not exist — a
        // secondary child collection, an EffectGraph algebra, a destination-side read — and a
        // property that accepted a value it then ignored would be worse than none at all, because a
        // scene would compile and render wrong. This test fails the moment one is added without the
        // machinery behind it, which is the reminder it exists to give.
        foreach (var absent in new[] { "Mask", "Effect", "Backdrop" })
        {
            Assert.IsNull(
                typeof(Node2D).GetProperty(absent),
                $"Node2D.{absent} is M2 and must not appear before the machinery that realizes it.");
        }

        foreach (var present in new[] { "Opacity", "Blend", "Clip", "Isolate", "ZIndex" })
        {
            Assert.IsNotNull(typeof(Node2D).GetProperty(present));
        }

        // A path clip is the part of §6.7's Clip that did not land: only a rectangle is expressible,
        // and the property's type says so rather than a doc comment hoping to be read.
        Assert.AreEqual(typeof(Rect?), typeof(Node2D).GetProperty("Clip")!.PropertyType);
    }
}
