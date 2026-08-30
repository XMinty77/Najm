using Najm.Core;
using Najm.Skia.Tests.Delivery;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// The offline entry points on <see cref="OfflineBackend.Gpu"/>: the configuration that used to be
/// unreachable through them, and that every GL-interop project therefore assembled by hand.
/// </summary>
/// <remarks>
/// <para>
/// These tests deliberately do not use <see cref="GpuFixture"/>'s provider. The whole subject is the
/// bring-up and the teardown the entry point now owns — the GL context, the Skia context over it,
/// and their reverse-order release — so handing it a context would test everything except the part
/// that was missing.
/// </para>
/// <para>
/// <see cref="GpuFixture.RequireStack"/> is still how they skip: it proves EGL can produce a context
/// on this box, then lets go of it, so the subject starts from the same clean slate a real caller
/// does.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class GpuOfflineTests
{
    [TestMethod]
    public void TheGpuBackendLoadsTheSceneIntoAnEnvironmentThatAdmitsItIsGpuBacked()
    {
        // N-3 and N-1 together, and the reason they belong in one test: a GPU offline run whose
        // environment reported RenderCaps.None was the exact configuration where a GL-texture
        // drawable is correct and where the attach-time check said it was impossible.
        GpuFixture.RequireStack();
        var scene = new CapsCapturingScene();
        var sink = new HashingFrameSink();

        var frames = SkiaOffline.Render(
            () => scene,
            new OfflineOptions { Sink = sink, Fps = 60d, Frames = 3L },
            OfflineBackend.Gpu);

        Assert.AreEqual(3L, frames);
        Assert.AreEqual(RenderCaps.SkiaSurface | RenderCaps.GpuBacked, scene.CapturedCaps);
        Assert.AreEqual(1, sink.BeginCount);
        Assert.AreEqual(1, sink.EndCount);
        Assert.HasCount(3, sink.Hashes);
    }

    [TestMethod]
    public void AGpuStillIsTheSameFrameAsARasterStillForAliasedGeometry()
    {
        // The frames must actually arrive, and be the frames. Axis-aligned aliased geometry has no
        // coverage estimate anywhere in it, so the two rasterizers have nothing to disagree about
        // and byte identity is the honest assertion — this is the same premise the provider suite's
        // cross-backend comparison rests on. It also catches the failure that would otherwise be
        // invisible: a GPU readback taken before the work was submitted is transparent black, and
        // transparent black is a perfectly plausible-looking frame.
        GpuFixture.RequireStack();
        using var scratch = new ScratchDirectory();
        var rasterPath = scratch.File("raster.png");
        var gpuPath = scratch.File("gpu.png");

        var rasterTicks = SkiaExport.Png(() => new WalkingPixelScene(), rasterPath, at: 0.5d, scale: 8f);
        var gpuTicks = SkiaExport.Png(
            () => new WalkingPixelScene(),
            gpuPath,
            at: 0.5d,
            scale: 8f,
            backend: OfflineBackend.Gpu);

        Assert.AreEqual(rasterTicks, gpuTicks, "The backend must not change the timing contract.");
        var difference = FrameProbe.Compare(gpuPath, rasterPath);
        Assert.IsTrue(
            difference.AreIdentical,
            $"The GPU still must be the raster still: {difference}.");
    }

    [TestMethod]
    public void EachRunOwnsAndReleasesItsGlStack_SoRunsDoNotPoisonEachOther()
    {
        // The dispose order the convenience took over — GRContext released while its GL context is
        // still alive, both from one provider disposal — is not observable from a single run: a
        // leaked or wrongly ordered teardown either crashes at process exit or leaves the next
        // bring-up on this thread with a context that is no longer current. A second run on the same
        // thread is what asks the question, and a raster run afterwards proves nothing global was
        // left broken.
        GpuFixture.RequireStack();
        using var scratch = new ScratchDirectory();
        var first = scratch.File("first.png");
        var second = scratch.File("second.png");
        var afterwards = scratch.File("afterwards.png");

        SkiaExport.Png(() => new WalkingPixelScene(), first, at: 0.25d, backend: OfflineBackend.Gpu);
        SkiaExport.Png(() => new WalkingPixelScene(), second, at: 0.25d, backend: OfflineBackend.Gpu);
        SkiaExport.Png(() => new WalkingPixelScene(), afterwards, at: 0.25d);

        Assert.IsTrue(FrameProbe.AreIdentical(first, second), "Two GPU runs of one scene must agree.");
        Assert.IsTrue(FrameProbe.AreIdentical(first, afterwards));
    }

    [TestMethod]
    public void SampleCountBecomesRealOnTheGpuBackend()
    {
        // On raster the parameter is inert by construction. Here it is a request the device either
        // honors or clamps, so the test asks the device what it can do before asserting anything:
        // a machine whose driver has no multisampled RGBA8 surfaces is not a failing machine.
        int maximum;
        using (var probe = GpuFixture.Require())
        {
            maximum = probe.Provider.MaxSampleCountFor(ColorSpace.Srgb);
        }

        if (maximum < 2)
        {
            Assert.Inconclusive(
                $"This device's largest sRGB surface sample count is {maximum}, so there is no "
                + "multisampling to observe.");
        }

        using var scratch = new ScratchDirectory();
        var single = scratch.File("1x.png");
        var multi = scratch.File($"{maximum}x.png");

        SkiaExport.Png(
            () => new DiagonalScene(),
            single,
            at: 0d,
            backend: OfflineBackend.Gpu,
            sampleCount: 1);
        SkiaExport.Png(
            () => new DiagonalScene(),
            multi,
            at: 0d,
            backend: OfflineBackend.Gpu,
            sampleCount: maximum);

        // The scene draws with antialiasing off, so at one sample its diagonal is a staircase of
        // fully covered or fully empty pixels. Multisampling is the only thing that can put an
        // intermediate value on that edge, and the difference is confined to the edge.
        var difference = FrameProbe.Compare(multi, single);
        Assert.IsGreaterThan(
            0,
            difference.DifferingPixels,
            $"A {maximum}-sample surface must resolve the diagonal differently from a single-sampled one.");
        Assert.IsLessThan(
            0.25d,
            difference.DifferingFraction,
            $"Multisampling must change an edge, not the whole frame: {difference}.");
    }
}
