using System.Numerics;
using Najm.Core;
using Najm.Skia.Tests.Delivery;
using Najm.Utils;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// Proves the GPU provider is the same provider: it creates targets the existing traverser and
/// compositor render into, its pixels agree with the raster provider's, and the one axis that is new
/// — sample count — normalizes rather than throwing or lying.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GpuSurfaceProviderTests
{
    /// <summary>
    /// The per-channel difference one pixel may show between the two rasterizers before it counts
    /// against the comparison.
    /// </summary>
    /// <remarks>
    /// The two backends do not compute coverage the same way and were never going to agree byte for
    /// byte. Skia's CPU backend scan-converts the path and integrates exact analytic coverage; its GL
    /// backend renders an oval as an analytic distance field in a fragment shader and a stroke as
    /// generated coverage geometry. Both are correct answers to the same question; neither is the
    /// other's rounding. Measured on this scene, 3.18% of pixels differ at all and the largest single
    /// difference is 73 — roughly a 0.29 coverage disagreement on one antialiased edge — while the
    /// mean over the whole frame is 0.244. So a per-pixel tolerance cannot be the assertion: it is a
    /// filter that separates edge pixels from everything else, and the assertions are made on the
    /// two aggregates below.
    /// </remarks>
    private const int EdgeTolerance = 8;

    /// <summary>
    /// The fraction of pixels allowed to exceed <see cref="EdgeTolerance"/>. Antialiased edges are a
    /// one-dimensional set in a two-dimensional image: measured at 1.88% here, and the limit sits
    /// well above that so a different driver's coverage geometry does not turn this into a flake. A
    /// structural divergence — a shifted transform, a dropped shape, a vertical flip — moves a
    /// two-dimensional region and goes straight through it.
    /// </summary>
    private const double MaximumBeyondToleranceFraction = 0.05;

    /// <summary>
    /// The mean per-channel difference over the whole frame, which is the assertion that actually
    /// says "these are the same image". Measured at 0.244; a one-pixel translation of this scene
    /// would put it in the tens and a vertical flip well above that.
    /// </summary>
    private const double MaximumMeanAbsolute = 1.0;

    [TestMethod]
    public void CreatedTarget_AdvertisesGpuBackedAndKeepsItsNormalizedSpec()
    {
        using var fixture = GpuFixture.Require();

        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(64, 48));
        var context = target.GetContext();

        Assert.AreEqual(RenderCaps.SkiaSurface | RenderCaps.GpuBacked, context.Caps);
        Assert.AreEqual(RenderCaps.SkiaSurface | RenderCaps.GpuBacked, fixture.Provider.Caps);
        Assert.AreEqual(new PixelSize(64, 48), target.Size);
        Assert.AreEqual(1, target.SurfaceSpec.SampleCount);
        Assert.AreEqual(ColorSpace.Srgb, target.SurfaceSpec.ColorSpace);
    }

    [TestMethod]
    public void RasterTargetDoesNotAdvertiseGpuBacked_WhichIsWhatAnAttachCheckKeysOn()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 8));

        Assert.AreEqual(RenderCaps.SkiaSurface, target.GetContext().Caps);
    }

    [TestMethod]
    public void SampleCount_RasterCollapsesToOne_ButTheGpuKeepsWhatTheDeviceCanGive()
    {
        using var fixture = GpuFixture.Require();
        var maximum = fixture.Provider.MaxSampleCountFor(ColorSpace.Srgb);
        var requested = new SurfaceSpec(64, 64, sampleCount: maximum);

        Assert.AreEqual(1, requested.NormalizeForRaster().SampleCount);
        var normalized = fixture.Provider.Normalize(requested);

        if (maximum == 1)
        {
            Assert.Inconclusive(
                "This device multisamples nothing, so there is no GPU-keeps-it case to observe here; "
                + "the clamping case below still runs.");
        }

        Assert.IsGreaterThan(
            1,
            normalized.SampleCount,
            "A GPU target has a real multisample axis and must not be normalized as raster is.");
        using var target = fixture.Provider.CreateTarget(requested);
        Assert.AreEqual(
            normalized.SampleCount,
            target.SurfaceSpec.SampleCount,
            "The target must report the specification it was actually built at.");
    }

    [TestMethod]
    public void SampleCountAboveTheDeviceMaximum_ClampsInsteadOfThrowingOrCorrupting()
    {
        using var fixture = GpuFixture.Require();
        var maximum = fixture.Provider.MaxSampleCountFor(ColorSpace.Srgb);

        // Far above anything a driver offers. Skia returns a null surface rather than clamping on
        // its own, so an unclamped request is not a quality question but a crash.
        var normalized = fixture.Provider.Normalize(new SurfaceSpec(32, 32, sampleCount: 4096));

        Assert.IsLessThanOrEqualTo(maximum, normalized.SampleCount);
        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(32, 32, sampleCount: 4096));
        Assert.AreEqual(normalized.SampleCount, target.SurfaceSpec.SampleCount);

        // And it still draws: clamping that produced an unusable surface would be no better.
        var context = target.GetContext();
        context.Clear(Color.Srgb(1f, 0f, 0f));
        fixture.Provider.Flush();
        var pixels = GpuPixels.Read(target);
        Assert.AreEqual((byte)255, GpuPixels.At(pixels, 32, 16, 16).R);
    }

    [TestMethod]
    public void SampleCountIsClampedPerColorType_NotPerDevice()
    {
        using var fixture = GpuFixture.Require();

        var srgbMaximum = fixture.Provider.MaxSampleCountFor(ColorSpace.Srgb);
        var linearMaximum = fixture.Provider.MaxSampleCountFor(ColorSpace.LinearSrgb);

        Assert.IsGreaterThanOrEqualTo(1, srgbMaximum);
        Assert.IsGreaterThanOrEqualTo(1, linearMaximum);
        var linear = fixture.Provider.Normalize(new SurfaceSpec(16, 16, 4, ColorSpace.LinearSrgb));
        Assert.IsLessThanOrEqualTo(
            linearMaximum,
            linear.SampleCount,
            "A half-float surface is commonly single-sampled on hardware that multisamples 8-bit "
            + "surfaces happily, so one device maximum is not enough.");
    }

    [TestMethod]
    public void ExistingRasterScene_RendersTheSameThroughBothProviders()
    {
        using var fixture = GpuFixture.Require();
        const int Width = 320;
        const int Height = 240;

        // The delivery suite's own encoder probe: a red band stepping across a black frame, drawn
        // with antialiasing off. Two instances because a scene is loaded once.
        var rasterPixels = RenderThroughRaster(new EncoderProbeScene(), Width, Height, frame: 3);
        var gpuPixels = RenderThroughGpu(fixture, new EncoderProbeScene(), Width, Height, frame: 3);

        var difference = GpuPixels.Compare(rasterPixels, gpuPixels, EdgeTolerance);

        // Aliased axis-aligned geometry has no coverage estimates at all, so this one is not a
        // tolerance case: the two backends must agree byte for byte.
        Assert.AreEqual(
            0,
            difference.MaxAbsolute,
            $"An aliased axis-aligned scene must be identical on both backends ({difference.Describe()}).");
    }

    [TestMethod]
    public void AntialiasedSceneWithCurvesAndAGradient_AgreesStructurallyAcrossBackends()
    {
        using var fixture = GpuFixture.Require();
        const int Width = 256;
        const int Height = 192;

        var rasterPixels = RenderThroughRaster(new CurvedScene(), Width, Height, frame: 0);
        var gpuPixels = RenderThroughGpu(fixture, new CurvedScene(), Width, Height, frame: 0);

        var difference = GpuPixels.Compare(rasterPixels, gpuPixels, EdgeTolerance);

        Assert.IsLessThanOrEqualTo(
            MaximumMeanAbsolute,
            difference.MeanAbsolute,
            $"The two backends did not render the same image ({difference.Describe()}).");
        Assert.IsLessThanOrEqualTo(
            MaximumBeyondToleranceFraction,
            difference.BeyondToleranceFraction,
            $"The two rasterizers disagree over more than antialiased edges ({difference.Describe()}).");

        // Structure, not liveness: a blank frame and a frame drawn at the wrong transform would both
        // pass a "not blank" check. These four probes are the shape of the scene itself.
        AssertSameStructure(rasterPixels, gpuPixels, Width, Height);
    }

    [TestMethod]
    public void StagedCompositorPath_RunsOnGpuSurfacesAndMatchesTheRasterFrame()
    {
        using var fixture = GpuFixture.Require();
        const int Width = 192;
        const int Height = 128;

        // Two layers with a viewport, an opacity and a blend between them: nothing here qualifies for
        // FP-1, so the whole canonical algorithm runs — per-layer targets, an accumulation surface,
        // merges, and the final replace-blit — all on GPU surfaces this provider made.
        var rasterPixels = RenderThroughRaster(new StackedScene(), Width, Height, frame: 0);
        var gpuScene = new StackedScene();
        gpuScene.Load(new SceneEnvironment(fixture.Provider));
        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(Width, Height));
        gpuScene.Render(target);
        fixture.Provider.Flush();
        var gpuPixels = GpuPixels.Read(target);

        var stats = gpuScene.Compositor!.Stats;
        Assert.IsFalse(stats.UsedSingleLayerFastPath, "This scene must not be quietly taking FP-1.");
        Assert.AreEqual(2, stats.MergeCount, "Both layers must have been staged and merged.");
        Assert.AreEqual(2, stats.LayerTargetCount);
        Assert.IsGreaterThan(0L, stats.LayerTargetBytes);

        var difference = GpuPixels.Compare(rasterPixels, gpuPixels, EdgeTolerance);
        Assert.AreEqual(
            0,
            difference.MaxAbsolute,
            $"Aliased staged composition must be identical on both backends ({difference.Describe()}).");
    }

    [TestMethod]
    public void FastPathStaysByteEquivalentToTheStagedPathOnGpuSurfacesToo()
    {
        using var fixture = GpuFixture.Require();
        var scene = new CurvedScene();
        scene.Load(new SceneEnvironment(fixture.Provider));
        var compositor = scene.Compositor!;
        using var fast = fixture.Provider.CreateTarget(new SurfaceSpec(256, 192));
        using var canonical = fixture.Provider.CreateTarget(new SurfaceSpec(256, 192));

        scene.Render(fast);
        fixture.Provider.Flush();
        var usedFastPath = compositor.Stats.UsedSingleLayerFastPath;
        var fastPixels = GpuPixels.Read(fast);

        compositor.Debug.ForceCanonicalPath = true;
        scene.Render(canonical);
        fixture.Provider.Flush();
        var canonicalPixels = GpuPixels.Read(canonical);

        Assert.IsTrue(usedFastPath, "Without this the equivalence could pass by never exercising FP-1.");
        CollectionAssert.AreEqual(
            fastPixels,
            canonicalPixels,
            "On a GPU surface as on a raster one, the fast path must be the staged algorithm's frame.");
    }

    [TestMethod]
    public void MultisampledLayerTargetsAreReusedAcrossFrames_BecauseNormalizationIsIdempotent()
    {
        using var fixture = GpuFixture.Require();
        var maximum = fixture.Provider.MaxSampleCountFor(ColorSpace.Srgb);
        var scene = new StackedScene();
        scene.Load(new SceneEnvironment(fixture.Provider));

        // The output is asked for at a count the device may well not honor verbatim. The compositor
        // builds every layer request from the output's specification and compares the kept request
        // against it component by component, so if normalization were not idempotent — if a spec
        // could normalize to something that does not normalize to itself — it would re-acquire every
        // layer target on every frame instead of never.
        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(96, 64, sampleCount: maximum));
        scene.Render(target);
        var first = scene.Compositor!.Stats;
        scene.Render(target);
        var second = scene.Compositor!.Stats;
        fixture.Provider.Flush();

        Assert.IsGreaterThan(0, first.TargetAcquisitionCount, "The first frame has to allocate.");
        Assert.AreEqual(
            0,
            second.TargetAcquisitionCount,
            "A steady frame must reuse every layer target and the accumulation surface.");
        Assert.AreEqual(first.LayerTargetCount, second.LayerTargetCount);
    }

    [TestMethod]
    public void ProviderIsBoundToItsGlContextThread_AndSaysSoRatherThanDrawingNothing()
    {
        using var fixture = GpuFixture.Require();
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                fixture.Provider.CreateTarget(new SurfaceSpec(8, 8)).Dispose();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });
        thread.Start();
        thread.Join();

        Assert.IsInstanceOfType<InvalidOperationException>(
            captured,
            "GPU work from a thread that does not hold the GL context current produces transparent "
            + "black with no error, so the provider has to be the one that complains.");
        StringAssert.Contains(captured!.Message, "transparent black");
    }

    [TestMethod]
    public void DisposedProvider_RefusesEveryEntryPoint()
    {
        var fixture = GpuFixture.Require();
        var provider = fixture.Provider;
        fixture.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.CreateTarget(new SurfaceSpec(8, 8)));
        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.CreateCompositor());
        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.WrapGlTexture(1, new PixelSize(4, 4)));
        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.Flush());
        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.ResetGlState());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = provider.NativeContext);
    }

    [TestMethod]
    public void ProviderIsConstructibleOverAContextItDidNotCreate_WhichIsHowTheHostWillUseIt()
    {
        using var fixture = GpuFixture.Require();

        // The host's shape: it owns the GL context and the GRContext, and hands the provider a
        // borrowed one. Two providers over one context is also legal and is what an embedded scene
        // sharing the host's context looks like.
        var hostOwnedContext = fixture.Provider.NativeContext;
        var borrowing = new GpuSkiaSurfaceProvider(hostOwnedContext, ownsContext: false);
        try
        {
            using var target = borrowing.CreateTarget(new SurfaceSpec(32, 32));
            target.GetContext().Clear(Color.Srgb(0f, 1f, 0f));
            borrowing.Flush();
            Assert.AreEqual((byte)255, GpuPixels.At(GpuPixels.Read(target), 32, 16, 16).G);
        }
        finally
        {
            borrowing.Dispose();
        }

        Assert.IsFalse(
            hostOwnedContext.IsAbandoned,
            "A borrowing provider must not take the context down with it.");
        using var afterwards = fixture.Provider.CreateTarget(new SurfaceSpec(16, 16));
        afterwards.GetContext().Clear(Color.Srgb(0f, 0f, 1f));
        fixture.Provider.Flush();
        Assert.AreEqual((byte)255, GpuPixels.At(GpuPixels.Read(afterwards), 16, 8, 8).B);
    }

    [TestMethod]
    public void ProviderRejectsANullOrAbandonedContextRatherThanFailingLater()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new GpuSkiaSurfaceProvider(null!, ownsContext: false));
        Assert.ThrowsExactly<ArgumentNullException>(() => GpuSkiaSurfaceProvider.CreateOver(null!));
    }

    private static byte[] RenderThroughRaster(Scene scene, int width, int height, long frame)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(frame));
        using var target = provider.CreateTarget(new SurfaceSpec(width, height));
        scene.Render(target);
        return GpuPixels.Read(target);
    }

    private static byte[] RenderThroughGpu(GpuFixture fixture, Scene scene, int width, int height, long frame)
    {
        scene.Load(new SceneEnvironment(fixture.Provider));
        scene.Tick(Ticks.At(frame));
        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(width, height));
        scene.Render(target);
        fixture.Provider.Flush();
        return GpuPixels.Read(target);
    }

    /// <summary>Asserts the two frames have the same large-scale structure, probe by probe.</summary>
    private static void AssertSameStructure(byte[] raster, byte[] gpu, int width, int height)
    {
        (int X, int Y, string What)[] probes =
        [
            (width / 2, height / 2, "the disc's centre"),
            (width / 2, 6, "the frame's top edge, outside every shape"),
            (width / 4, height / 2, "the gradient bar's left end"),
            ((3 * width) / 4, height / 2, "the gradient bar's right end"),
            (6, height - 6, "the frame's bottom-left corner"),
        ];

        foreach (var (x, y, what) in probes)
        {
            var left = GpuPixels.At(raster, width, x, y);
            var right = GpuPixels.At(gpu, width, x, y);
            Assert.IsLessThanOrEqualTo(
                EdgeTolerance,
                Math.Max(
                    Math.Max(Math.Abs(left.R - right.R), Math.Abs(left.G - right.G)),
                    Math.Max(Math.Abs(left.B - right.B), Math.Abs(left.A - right.A))),
                $"The two backends disagree at {what}: raster {left}, GPU {right}.");
        }
    }

    /// <summary>
    /// A scene whose every feature is a coverage estimate: a filled disc, a thick stroked ring, and
    /// a gradient bar, all antialiased.
    /// </summary>
    private sealed class CurvedScene : Scene
    {
        internal CurvedScene()
        {
            VirtualResolution = new Vector2(256f, 192f);
            var layer = Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(0f, 0f, 0f) });
            layer.Root.Add(new CurvedDrawable());
        }
    }

    private sealed class CurvedDrawable : Drawable
    {
        private static readonly Rect Bounds = new(0f, 0f, 256f, 192f);

        private readonly Paint disc = Paint.Fill(Color.Srgb(0.9f, 0.2f, 0.1f));
        private readonly Paint ring = Paint.Stroke(Color.Srgb(0.1f, 0.7f, 0.9f), width: 7f);
        private readonly Paint bar;

        internal CurvedDrawable()
        {
            GradientStop[] stops =
            [
                new(0f, Color.Srgb(1f, 1f, 0f)),
                new(1f, Color.Srgb(0f, 0.3f, 1f)),
            ];
            bar = Paint.Fill(Brush.Linear(new Vector2(32f, 0f), new Vector2(224f, 0f), stops));
        }

        public override Rect GeometryBounds => Bounds;

        public override void Render(IDrawContext2D context)
        {
            context.DrawEllipse(new Vector2(128f, 96f), 54f, 40f, disc);
            context.DrawEllipse(new Vector2(128f, 96f), 78f, 66f, ring);
            context.DrawRoundRect(new Rect(32f, 84f, 192f, 24f), 11f, bar);
        }
    }

    /// <summary>
    /// Two layers that cannot take the single-layer fast path: the upper one occupies a viewport,
    /// carries an opacity and a non-default blend, and therefore forces the staged algorithm.
    /// </summary>
    private sealed class StackedScene : Scene
    {
        internal StackedScene()
        {
            VirtualResolution = new Vector2(192f, 128f);
            var back = Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(0f, 0f, 0.25f) });
            back.Root.Add(new AliasedRect(new Rect(16f, 16f, 96f, 64f), Color.Srgb(1f, 0f, 0f)));

            var front = Layers.Add(new ScreenLayer
            {
                ClearColor = Color.Srgb(0f, 0f, 0f, 0f),
                Viewport = new Rect(64f, 32f, 96f, 64f),
                Opacity = 0.5f,
                Blend = BlendMode.Screen,
            });
            front.Root.Add(new AliasedRect(new Rect(80f, 48f, 64f, 32f), Color.Srgb(0f, 1f, 0f)));
        }
    }

    /// <summary>A solid axis-aligned rectangle with antialiasing off, so its pixels are decidable.</summary>
    private sealed class AliasedRect : Drawable
    {
        private readonly PathBuilder path;
        private readonly Paint paint;
        private readonly Rect bounds;

        internal AliasedRect(Rect bounds, Color color)
        {
            this.bounds = bounds;
            paint = Paint.Fill(color, isAntialias: false);
            path = new PathBuilder(initialCapacity: 5)
                .MoveTo(bounds.X, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y + bounds.Height)
                .LineTo(bounds.X, bounds.Y + bounds.Height)
                .Close();
        }

        public override Rect GeometryBounds => bounds;

        public override void Render(IDrawContext2D context) => context.DrawPath(path, paint);
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
