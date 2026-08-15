namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class SurfaceSpecTests
{
    [TestMethod]
    public void NormalizeForRaster_PreservesDimensionsAndColorSpace_ButUsesOneSample()
    {
        var requested = new SurfaceSpec(640, 360, sampleCount: 4, ColorSpace.LinearSrgb);

        var normalized = requested.NormalizeForRaster();

        Assert.AreEqual(new PixelSize(640, 360), normalized.Size);
        Assert.AreEqual(1, normalized.SampleCount);
        Assert.AreEqual(ColorSpace.LinearSrgb, normalized.ColorSpace);
        Assert.IsTrue(normalized.IsValid);
    }

    [TestMethod]
    public void ZeroInitializedSpec_CannotBeNormalizedOrExposeSize()
    {
        var spec = default(SurfaceSpec);

        Assert.IsFalse(spec.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => spec.NormalizeForRaster());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = spec.Size);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidDimensionsSamplesAndColorSpace()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SurfaceSpec(0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SurfaceSpec(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SurfaceSpec(1, 1, 0));
        Assert.ThrowsExactly<ArgumentException>(() => new SurfaceSpec(1, 1, 1, (ColorSpace)99));
    }
}
