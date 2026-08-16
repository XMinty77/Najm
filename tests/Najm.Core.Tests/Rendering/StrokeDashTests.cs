namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class StrokeDashTests
{
    [TestMethod]
    public void IndependentlyConstructedPatterns_AreEqualAndHashEqual()
    {
        var first = new StrokeDash([4f, 2f, 1f, 2f], phase: 1.5f);
        var second = new StrokeDash([4f, 2f, 1f, 2f], phase: 1.5f);

        Assert.AreEqual(first, second, "Dashes must compare by interval contents, not by array reference.");
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(first == second);
        Assert.AreNotEqual(first, new StrokeDash([4f, 2f, 1f, 2f]));
        Assert.AreNotEqual(first, new StrokeDash([4f, 2f], phase: 1.5f));
    }

    [TestMethod]
    public void Default_IsEmptyAndCarriesNoIntervals()
    {
        var dash = default(StrokeDash);

        Assert.IsTrue(dash.IsEmpty);
        Assert.AreEqual(0, dash.Intervals.Length);
        Assert.AreEqual(0f, dash.Phase);
    }

    [TestMethod]
    public void Intervals_MustBePairedFiniteAndPositive()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new StrokeDash([2f]));
        Assert.ThrowsExactly<ArgumentException>(() => new StrokeDash([2f, 1f, 3f]));
        Assert.ThrowsExactly<ArgumentException>(() => new StrokeDash(default));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([2f, 0f]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([2f, -1f]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([float.NaN, 1f]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([float.PositiveInfinity, 1f]));
    }

    [TestMethod]
    public void Phase_MustBeFiniteAndNonnegative()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([2f, 2f], phase: -0.5f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StrokeDash([2f, 2f], phase: float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new StrokeDash([2f, 2f], phase: float.PositiveInfinity));
    }

    [TestMethod]
    public void MutatingTheCallerArray_DoesNotChangeThePattern()
    {
        var intervals = new[] { 3f, 1f };
        var dash = new StrokeDash(intervals);

        intervals[0] = 9f;

        Assert.AreEqual(new StrokeDash([3f, 1f]), dash);
    }
}
