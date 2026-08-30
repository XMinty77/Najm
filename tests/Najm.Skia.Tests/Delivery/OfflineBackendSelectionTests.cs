using Najm.Core;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The backend parameter's raster half, and the argument checking, both of which are answerable
/// without a GL stack. What the GPU half actually renders is <c>GpuOfflineTests</c>' job.
/// </summary>
/// <remarks>
/// The parameter's whole promise is that it changes the backend and nothing else, so the tests worth
/// having here are the ones that prove nothing else changed: the explicit default is the default,
/// and an undefined value is refused where the caller can see it rather than surfacing as a missing
/// provider deeper in.
/// </remarks>
[TestClass]
public sealed class OfflineBackendSelectionTests
{
    [TestMethod]
    public void AskingForRasterExplicitlyIsTheSameRunAsAskingForNothing()
    {
        using var scratch = new ScratchDirectory();
        var implicitPath = scratch.File("implicit.png");
        var explicitPath = scratch.File("explicit.png");

        var implicitTicks = SkiaExport.Png(() => new WalkingPixelScene(), implicitPath, at: 0.25d);
        var explicitTicks = SkiaExport.Png(
            () => new WalkingPixelScene(),
            explicitPath,
            at: 0.25d,
            backend: OfflineBackend.Raster);

        Assert.AreEqual(implicitTicks, explicitTicks);
        Assert.IsTrue(
            FrameProbe.AreIdentical(implicitPath, explicitPath),
            "The default backend and OfflineBackend.Raster must be the same configuration.");
    }

    [TestMethod]
    public void RasterNormalizesEverySampleCountToOne_SoAskingForFourChangesNothing()
    {
        // The parameter is new on this entry point and it is inert here by design: raster Skia is
        // analytically antialiased and has no multisample axis, so the honest behaviour is to
        // normalize rather than to refuse. It becomes a real request on the GPU backend.
        using var scratch = new ScratchDirectory();
        var single = scratch.File("single.png");
        var multi = scratch.File("multi.png");

        SkiaExport.Png(() => new WalkingPixelScene(), single, at: 0d, sampleCount: 1);
        SkiaExport.Png(() => new WalkingPixelScene(), multi, at: 0d, sampleCount: 4);

        Assert.IsTrue(FrameProbe.AreIdentical(single, multi));
    }

    [TestMethod]
    public void AnUndefinedBackendIsRefusedByBothEntryPoints()
    {
        using var scratch = new ScratchDirectory();
        const OfflineBackend Undefined = (OfflineBackend)7;

        var fromRender = Assert.ThrowsExactly<ArgumentException>(
            () => SkiaOffline.Render(
                () => new WalkingPixelScene(),
                new OfflineOptions { Sink = new HashingFrameSink(), Frames = 1L },
                Undefined));
        var fromPng = Assert.ThrowsExactly<ArgumentException>(
            () => SkiaExport.Png(() => new WalkingPixelScene(), scratch.File("never.png"), at: 0d, backend: Undefined));

        Assert.AreEqual("backend", fromRender.ParamName);
        Assert.AreEqual("backend", fromPng.ParamName);
        Assert.IsFalse(File.Exists(scratch.File("never.png")), "A refused run must not have written anything.");
    }

    [TestMethod]
    public void TheRasterBackendReportsItsCapabilitiesToTheLoadedScene()
    {
        // The other half of the capability seam: forwarding is only worth anything if the raster
        // provider's answer is also true. SkiaSurface, because a SkiaDrawable is legal here; not
        // GpuBacked, which is what a wrapped GL texture keys its refusal on.
        var scene = new CapsCapturingScene();

        SkiaOffline.Render(
            () => scene,
            new OfflineOptions { Sink = new HashingFrameSink(), Frames = 1L });

        Assert.AreEqual(RenderCaps.SkiaSurface, scene.CapturedCaps);
    }
}

/// <summary>Records the capabilities its environment reported at load, and draws nothing.</summary>
internal sealed class CapsCapturingScene : Scene
{
    internal RenderCaps CapturedCaps { get; private set; } = (RenderCaps)(-1);

    protected override void OnLoad() => CapturedCaps = Env.Caps;
}
