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

    [TestMethod]
    public void NormalizeForGpu_KeepsTheMultisampleAxisRasterDoesNotHave()
    {
        var requested = new SurfaceSpec(640, 360, sampleCount: 4);

        var gpu = requested.NormalizeForGpu(maxSampleCount: 8);

        Assert.AreEqual(4, gpu.SampleCount, "A GPU target has a real multisample axis.");
        Assert.AreEqual(1, requested.NormalizeForRaster().SampleCount, "CPU raster has none.");
        Assert.AreEqual(new PixelSize(640, 360), gpu.Size);
        Assert.AreEqual(ColorSpace.Srgb, gpu.ColorSpace);
    }

    [TestMethod]
    public void NormalizeForGpu_ClampsAboveTheDeviceMaximumRatherThanThrowing()
    {
        // The device supports 4. Asking for 64 gets 4, not an exception and not a surface the
        // driver silently declines to make.
        var normalized = new SurfaceSpec(64, 64, sampleCount: 64).NormalizeForGpu(maxSampleCount: 4);

        Assert.AreEqual(4, normalized.SampleCount);
    }

    [TestMethod]
    public void NormalizeForGpu_RoundsDownToAPowerOfTwoSoTheRecordedCountIsTheRealisedOne()
    {
        // Three is legal to ask for and impossible to get: a backend handed three supplies four and
        // the specification would then describe a surface that does not exist. Two does exist.
        Assert.AreEqual(2, new SurfaceSpec(8, 8, sampleCount: 3).NormalizeForGpu(4).SampleCount);
        Assert.AreEqual(4, new SurfaceSpec(8, 8, sampleCount: 7).NormalizeForGpu(8).SampleCount);
        Assert.AreEqual(1, new SurfaceSpec(8, 8, sampleCount: 1).NormalizeForGpu(4).SampleCount);
        Assert.AreEqual(1, new SurfaceSpec(8, 8, sampleCount: 8).NormalizeForGpu(1).SampleCount);
    }

    [TestMethod]
    public void NormalizeForGpu_IsIdempotent_WhichIsWhatCompositorSpecMatchingRelies()
    {
        foreach (var requested in new[] { 1, 2, 3, 4, 5, 8, 13, 64 })
        {
            var once = new SurfaceSpec(32, 32, requested).NormalizeForGpu(4);
            var twice = once.NormalizeForGpu(4);

            Assert.AreEqual(once, twice, $"Normalizing {requested} twice must equal normalizing it once.");
        }
    }

    [TestMethod]
    public void NormalizeForGpu_RejectsANonPositiveDeviceMaximumAndAnInvalidSpec()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SurfaceSpec(8, 8).NormalizeForGpu(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => default(SurfaceSpec).NormalizeForGpu(4));
    }
}
