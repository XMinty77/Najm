using System.Numerics;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class RectTests
{
    [TestMethod]
    public void Constructor_ExposesFiniteEdgesAndEmptyState()
    {
        var rect = new Rect(-2f, 3f, 5f, 7f);

        Assert.AreEqual(-2f, rect.Left);
        Assert.AreEqual(3f, rect.Top);
        Assert.AreEqual(3f, rect.Right);
        Assert.AreEqual(10f, rect.Bottom);
        Assert.IsFalse(rect.IsEmpty);
        Assert.IsTrue(default(Rect).IsEmpty);
        Assert.IsTrue(new Rect(1f, 2f, 0f, 4f).IsEmpty);
    }

    [TestMethod]
    public void Constructor_RejectsNonfiniteCoordinatesSizesAndEdges()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Rect(float.NaN, 0f, 1f, 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Rect(0f, float.PositiveInfinity, 1f, 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Rect(0f, 0f, -1f, 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Rect(0f, 0f, 1f, float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Rect(float.MaxValue, 0f, float.MaxValue, 1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Rect(0f, float.MaxValue, 1f, float.MaxValue));
    }

    [TestMethod]
    public void ContainsIsHalfOpenSoAnEmptyRectangleContainsNothing()
    {
        var rect = new Rect(-2f, 3f, 5f, 7f);

        Assert.IsTrue(rect.Contains(new Vector2(-2f, 3f)), "The top-left corner is inside.");
        Assert.IsTrue(rect.Contains(new Vector2(0f, 5f)));
        Assert.IsTrue(rect.Contains(new Vector2(2.999f, 9.999f)));

        Assert.IsFalse(rect.Contains(new Vector2(3f, 5f)), "The right edge is outside.");
        Assert.IsFalse(rect.Contains(new Vector2(0f, 10f)), "The bottom edge is outside.");
        Assert.IsFalse(rect.Contains(new Vector2(-2.001f, 5f)));
        Assert.IsFalse(rect.Contains(new Vector2(0f, 2.999f)));
        Assert.IsFalse(rect.Contains(new Vector2(float.NaN, 5f)));

        // This is the case the half-open rule exists for: Node2D.HitBounds defaults to
        // default(Rect), and a closed test would make every plain node a hit at its own origin.
        Assert.IsFalse(default(Rect).Contains(Vector2.Zero));
        Assert.IsFalse(new Rect(4f, 4f, 0f, 10f).Contains(new Vector2(4f, 6f)));

        // Tiling rectangles partition rather than share a seam.
        Assert.IsTrue(new Rect(0f, 0f, 10f, 10f).Contains(new Vector2(9.5f, 0f)));
        Assert.IsFalse(new Rect(0f, 0f, 10f, 10f).Contains(new Vector2(10f, 0f)));
        Assert.IsTrue(new Rect(10f, 0f, 10f, 10f).Contains(new Vector2(10f, 0f)));
    }
}
