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
}
