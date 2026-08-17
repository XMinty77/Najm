using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Author-visible proof of the M1 ordinary-layer compositor: layer opacity, blend, viewport,
/// clear-color contribution, path equivalence, and target lifecycle, all at the pixel level. Every
/// expectation is derived from the compositing arithmetic in the comment above it — premultiplied
/// source-over is <c>out = src·α + dst·(1 − src.a·α)</c> — never captured from a previous run.
/// </summary>
[TestClass]
public sealed class LayerCompositionTests
{
    private const string Black = "000000ff";
    private const string Red = "ff0000ff";
    private const string Green = "00ff00ff";
    private const string Blue = "0000ffff";
    private const string Transparent = "00000000";

    /// <summary>0.2 red over 0.8 of an opaque-red backdrop: (0,255,0)·0.2 + (255,0,0)·0.8.</summary>
    private const string GreenOverRedAtOneFifth = "cc3300ff";

    /// <summary>(255,128,0) × (128,255,255), per channel, normalized.</summary>
    private const string OrangeTimesCyan = "808000ff";

    /// <summary>(255,0,0) screened with (0,128,0): s + d − s·d, per channel.</summary>
    private const string RedScreenedWithHalfGreen = "ff8000ff";

    /// <summary>128/255 exactly, so a float channel round-trips to the byte 128 with no ambiguity.</summary>
    private const float HalfChannel = 128f / 255f;

    private static readonly Color OpaqueBlack = Color.Srgb(0f, 0f, 0f);
    private static readonly Color OpaqueRed = Color.Srgb(1f, 0f, 0f);
    private static readonly Color OpaqueGreen = Color.Srgb(0f, 1f, 0f);
    private static readonly Color OpaqueBlue = Color.Srgb(0f, 0f, 1f);

    [TestMethod]
    public void TwoOpaqueLayers_CompositeBottomToTopInAddOrder()
    {
        // 4×2 virtual space on a 4×2 target: renderScale 1, virtual units are pixels.
        // Bottom layer: clears the frame red, then paints columns 0-1 blue.
        // Top layer: clears transparent, then paints columns 1-2 green.
        // Column 0 shows the bottom layer's node, column 1 shows the top layer covering it,
        // column 2 shows the top layer over the bottom's clear, column 3 shows the clear itself.
        const string ExpectedRow = Blue + Green + Green + Red;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var bottom = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        bottom.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueBlue));
        var top = scene.Layers.Add(new ScreenLayer());
        top.Root.Add(new RectDrawable(new Rect(1f, 0f, 2f, 2f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(ExpectedRow + ExpectedRow, Hex(target));

        var stats = Stats(scene);
        Assert.AreEqual(2, stats.MergeCount, "Both layers must merge into the accumulation surface.");
        Assert.AreEqual(2, stats.LayerTargetCount);
        Assert.IsFalse(stats.UsedSingleLayerFastPath, "Two visible layers cannot take FP-1.");
    }

    [TestMethod]
    public void LayerOpacity_AttenuatesTheWholeLayerOverTheBackdropBelowIt()
    {
        // Backdrop: opaque red (255,0,0,255). Layer: opaque green over columns 0-1, Opacity 0.2.
        // Premultiplied source-over with a uniform group alpha of 0.2 and an opaque source:
        //   out = src·0.2 + dst·(1 − 1·0.2) = (0,255,0)·0.2 + (255,0,0)·0.8 = (51 green, 204 red).
        // 0.2 and 0.8 are exact fifths of 255 (51 and 204), so the expectation carries no rounding
        // question. Where the layer is transparent its premultiplied source is zero and the
        // backdrop must survive untouched.
        const string ExpectedRow = GreenOverRedAtOneFifth + GreenOverRedAtOneFifth + Red + Red;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var fading = scene.Layers.Add(new ScreenLayer { Opacity = 0.2f });
        fading.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(ExpectedRow + ExpectedRow, Hex(target));
    }

    [TestMethod]
    public void LayerBlendMultiply_MultipliesEachChannelAgainstTheLayersBelow()
    {
        // Backdrop (255,128,0) under an opaque (128,255,255) layer blended Multiply. Both are
        // opaque, so premultiplied multiply reduces to the per-channel product:
        //   R = 255·(128/255) = 128, G = 128·(255/255) = 128, B = 0·1 = 0, A = 1.
        // Each product keeps one operand at 0 or 255, so every channel is exact in eight bits.
        var scene = new Scene { VirtualResolution = new Vector2(2f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(1f, HalfChannel, 0f) });
        scene.Layers.Add(new ScreenLayer
        {
            ClearColor = Color.Srgb(HalfChannel, 1f, 1f),
            Blend = BlendMode.Multiply,
        });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(2, 1));
        scene.Render(target);

        Assert.AreEqual(OrangeTimesCyan + OrangeTimesCyan, Hex(target));
    }

    [TestMethod]
    public void LayerBlendScreen_ScreensEachChannelAgainstTheLayersBelow()
    {
        // Backdrop (255,0,0) under an opaque (0,128,0) layer blended Screen: out = s + d − s·d.
        //   R = 0 + 255 − 0 = 255, G = 128 + 0 − 0 = 128, B = 0, A = 1 + 1 − 1 = 1.
        var scene = new Scene { VirtualResolution = new Vector2(2f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        scene.Layers.Add(new ScreenLayer
        {
            ClearColor = Color.Srgb(0f, HalfChannel, 0f),
            Blend = BlendMode.Screen,
        });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(2, 1));
        scene.Render(target);

        Assert.AreEqual(RedScreenedWithHalfGreen + RedScreenedWithHalfGreen, Hex(target));
    }

    [TestMethod]
    public void ViewportLayer_LandsOneToOneInItsRectAndPaintsNothingOutsideIt()
    {
        // 8×4 virtual space at renderScale 1 over an opaque-red frame. The upper layer occupies the
        // virtual rect (2,1)-(5,3): its target is 3×2 device pixels placed at device (2,1), and it
        // renders through the same absolute transforms a full-frame layer would, shifted onto that
        // target. So its blue clear fills exactly x ∈ [2,5), y ∈ [1,3); its unit square at layer
        // coordinate (2,1) lands on the viewport's own first pixel, (2,1); and its unit square at
        // (6,3) is outside the target altogether and cannot reach the frame.
        const string ExpectedRows =
            Red + Red + Red + Red + Red + Red + Red + Red +
            Red + Red + Green + Blue + Blue + Red + Red + Red +
            Red + Red + Blue + Blue + Blue + Red + Red + Red +
            Red + Red + Red + Red + Red + Red + Red + Red;

        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var framed = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(2f, 1f, 3f, 2f),
        });
        framed.Root.Add(new RectDrawable(new Rect(2f, 1f, 1f, 1f), OpaqueGreen));
        framed.Root.Add(new RectDrawable(new Rect(6f, 3f, 1f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(target);

        Assert.AreEqual(ExpectedRows, Hex(target));

        // The viewport's target is its own size, not the frame's: 3×2 pixels of RGBA8888.
        Assert.AreEqual(
            (8L * 4L * 4L) + (3L * 2L * 4L),
            Stats(scene).LayerTargetBytes,
            "A viewport'd layer must be staged through a viewport-sized target.");
    }

    [TestMethod]
    public void ViewportWorldLayer_FramesItsViewportRatherThanCroppingTheFrame()
    {
        // 8×4 virtual space at renderScale 1 over an opaque-red frame. The upper layer is a
        // WorldLayer2D occupying the virtual rect (4,0)-(8,4), so its camera frames a 4×4 extent —
        // the viewport's, not the scene's — and the viewport's own centre is virtual (2,2) inside
        // that extent. The camera sits at the world origin at zoom 1, so world (0,0) lands there,
        // which is frame virtual (4+2, 0+2) = (6,2).
        //
        // The 2×2 world square spans world (-1,-1)-(1,1). One world unit is one virtual unit and
        // world +Y maps to virtual -Y, so it covers viewport-local [1,3)×[1,3) and therefore frame
        // pixels x ∈ [5,7), y ∈ [1,3). The layer's blue clear fills its whole viewport, x ∈ [4,8).
        //
        // The defect this pins: framing against the scene's 8×4 instead would centre the square on
        // the frame at virtual (4,2), spanning x ∈ [3,5) — of which only the single column x = 4
        // falls inside the viewport's target. The wrong reading is one green column at x = 4 in
        // rows 1-2; the right one is a 2×2 green block at x ∈ [5,7). A ScreenLayer cannot tell the
        // two apart, because it has no camera and nothing to reframe.
        const string ExpectedRows =
            Red + Red + Red + Red + Blue + Blue + Blue + Blue +
            Red + Red + Red + Red + Blue + Green + Green + Blue +
            Red + Red + Red + Red + Blue + Green + Green + Blue +
            Red + Red + Red + Red + Blue + Blue + Blue + Blue;

        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var framed = scene.Layers.Add(new WorldLayer2D
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(4f, 0f, 4f, 4f),
        });
        framed.Root.Add(new RectDrawable(new Rect(-1f, -1f, 2f, 2f), OpaqueGreen));

        Assert.AreEqual(Vector2.Zero, framed.Camera.Position);
        Assert.AreEqual(1f, framed.Camera.Zoom);

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(target);

        Assert.AreEqual(ExpectedRows, Hex(target));

        // Its target is the viewport's own 4×4 pixels, and the frame-sized layer below it 8×4.
        Assert.AreEqual(
            (8L * 4L * 4L) + (4L * 4L * 4L),
            Stats(scene).LayerTargetBytes,
            "A viewport'd layer must be staged through a viewport-sized target.");
    }

    [TestMethod]
    public void MovingAViewportBetweenFrames_LeavesNothingOfThePreviousFrameBehind()
    {
        // The only contributing layer occupies a 2×2 viewport over an otherwise empty 8×4 frame, so
        // every pixel outside the viewport is transparent — the accumulation surface starts each
        // frame cleared and the output is replaced rather than painted over. Moving the viewport
        // must therefore move the blue block outright: a stale block at the old position would mean
        // either the accumulation surface or the output carried a previous frame forward.
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var framed = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(0f, 0f, 2f, 2f),
        });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(target);

        Assert.AreEqual(
            Blue + Blue + Repeat(Transparent, 6) +
            Blue + Blue + Repeat(Transparent, 6) +
            Repeat(Transparent, 16),
            Hex(target));

        framed.Viewport = new Rect(4f, 1f, 2f, 2f);
        scene.Render(target);

        Assert.AreEqual(
            Repeat(Transparent, 8) +
            Repeat(Transparent, 4) + Blue + Blue + Transparent + Transparent +
            Repeat(Transparent, 4) + Blue + Blue + Transparent + Transparent +
            Repeat(Transparent, 8),
            Hex(target));
        Assert.AreEqual(
            0,
            Stats(scene).TargetAcquisitionCount,
            "A viewport that moves without changing size must reuse its target.");
    }

    [TestMethod]
    public void InvisibleAndZeroOpacityLayers_ContributeLiterallyNothing()
    {
        // The upper layer's opaque blue clear would cover the frame outright. While it cannot
        // contribute, the frame is the red layer below it and the upper layer is never bound, never
        // cleared, and never merged — so it holds no target at all. The canonical path is forced
        // because a single contributing layer would otherwise qualify for FP-1, which would prove
        // the degeneracy against the wrong algorithm.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var covering = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlue });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        Compositor(scene).Debug.ForceCanonicalPath = true;
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));

        covering.Visible = false;
        scene.Render(target);

        Assert.AreEqual(Repeat(Red, 8), Hex(target), "An invisible layer contributes nothing.");
        Assert.AreEqual(1, Stats(scene).MergeCount);
        Assert.AreEqual(1, Stats(scene).LayerTargetCount, "A skipped layer is never bound.");

        covering.Visible = true;
        covering.Opacity = 0f;
        scene.Render(target);

        Assert.AreEqual(Repeat(Red, 8), Hex(target), "A zero-opacity layer contributes nothing either.");
        Assert.AreEqual(1, Stats(scene).MergeCount);
        Assert.AreEqual(1, Stats(scene).LayerTargetCount);

        covering.Opacity = 1f;
        scene.Render(target);

        Assert.AreEqual(Repeat(Blue, 8), Hex(target), "Restoring the layer restores its clear.");
        Assert.AreEqual(2, Stats(scene).MergeCount);
        Assert.AreEqual(2, Stats(scene).LayerTargetCount);
    }

    [TestMethod]
    public void ALayerWhoseSubtreeDrawsNothing_StillContributesItsClearColor()
    {
        // The upper layer has an empty tree, so its whole contribution is its clear color — which is
        // content, not an absence. An opaque blue clear over the red frame below must therefore
        // leave a blue frame, and the layer must have been bound and cleared to produce it.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlue });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(Repeat(Blue, 8), Hex(target));
        Assert.AreEqual(2, Stats(scene).MergeCount);
        Assert.AreEqual(2, Stats(scene).LayerTargetCount);
    }

    [TestMethod]
    public void SingleLayerFastPath_IsByteIdenticalToTheCanonicalPathAndIsGenuinelyTaken()
    {
        // One full-frame layer at opacity one with the default blend and the output's own surface
        // spec: FP-1 territory. 4×2 virtual space at renderScale 1, a black clear with columns 0-1
        // red, so the frame is derivable independently of either path.
        const string ExpectedRow = Red + Red + Black + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueRed));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var compositor = Compositor(scene);
        using var fast = provider.CreateTarget(new SurfaceSpec(4, 2));
        using var canonical = provider.CreateTarget(new SurfaceSpec(4, 2));

        scene.Render(fast);
        var fastStats = compositor.Stats;

        compositor.Debug.ForceCanonicalPath = true;
        scene.Render(canonical);
        var canonicalStats = compositor.Stats;

        Assert.AreEqual(ExpectedRow + ExpectedRow, Hex(fast));
        CollectionAssert.AreEqual(
            ReadRgba(fast),
            ReadRgba(canonical),
            "The fast path must be byte-identical to the staged algorithm it replaces.");

        Assert.IsTrue(
            fastStats.UsedSingleLayerFastPath,
            "Without this the equivalence could pass by never exercising FP-1 at all.");
        Assert.AreEqual(0, fastStats.MergeCount, "FP-1 skips the accumulation surface entirely.");
        Assert.AreEqual(0, fastStats.LayerTargetCount, "FP-1 skips the layer target entirely.");
        Assert.AreEqual(0L, fastStats.LayerTargetBytes);

        Assert.IsFalse(canonicalStats.UsedSingleLayerFastPath, "ForceCanonicalPath must switch FP-1 off.");
        Assert.AreEqual(1, canonicalStats.MergeCount);
        Assert.AreEqual(1, canonicalStats.LayerTargetCount);
    }

    [TestMethod]
    public void FastPathIsRefused_WhenTheLayerCarriesPresentationTheOutputCannotAbsorb()
    {
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var compositor = Compositor(scene);
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));

        scene.Render(target);
        Assert.IsTrue(compositor.Stats.UsedSingleLayerFastPath, "The unmodified layer qualifies.");

        layer.Opacity = 0.5f;
        scene.Render(target);
        Assert.IsFalse(compositor.Stats.UsedSingleLayerFastPath, "Opacity below one must merge.");

        layer.Opacity = 1f;
        layer.Blend = BlendMode.Multiply;
        scene.Render(target);
        Assert.IsFalse(compositor.Stats.UsedSingleLayerFastPath, "A non-default blend must merge.");

        layer.Blend = BlendMode.SrcOver;
        layer.Viewport = new Rect(0f, 0f, 2f, 1f);
        scene.Render(target);
        Assert.IsFalse(compositor.Stats.UsedSingleLayerFastPath, "A viewport'd layer must be placed.");

        layer.Viewport = null;
        scene.Render(target);
        Assert.IsTrue(compositor.Stats.UsedSingleLayerFastPath, "Removing the disqualifier restores FP-1.");
    }

    [TestMethod]
    public void TickOnceRenderTwiceThroughTheCompositor_IsByteIdentical()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var backdrop = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        backdrop.Root.Add(new RectDrawable(new Rect(1f, 1f, 4f, 2f), OpaqueRed));
        var world = scene.Layers.Add(new WorldLayer2D { Opacity = 0.5f, Blend = BlendMode.Screen });
        world.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen)).Position =
            new Vector2(-1f, 0f);
        var overlay = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(5f, 0f, 3f, 4f),
            Opacity = 0.25f,
        });
        overlay.Root.Add(new RectDrawable(new Rect(6f, 1f, 1f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));

        using var first = provider.CreateTarget(new SurfaceSpec(8, 4));
        using var second = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(first);
        var firstPixels = ReadRgba(first);
        scene.Render(second);

        CollectionAssert.AreEqual(
            firstPixels,
            ReadRgba(second),
            "Two composited renders of one ticked frame must be byte identical.");

        scene.Render(first);

        CollectionAssert.AreEqual(
            firstPixels,
            ReadRgba(first),
            "Re-rendering into the same target must be stable, including the accumulation surface reuse.");
        Assert.AreEqual(SceneState.Started, scene.State);
    }

    [TestMethod]
    public void LayerTargets_AreReusedAtAStableSizeAndReacquiredWhenTheRenderScaleChanges()
    {
        // Two layers over an 8×4 virtual space. At renderScale 1 each layer target is 8×4 RGBA8888
        // = 128 bytes, and the first frame acquires three surfaces: one per layer plus the
        // accumulation surface. Doubling the output doubles the render scale to
        // min(16/8, 8/4) = 2, so each layer target becomes 16×8 = 512 bytes and all three surfaces
        // are re-acquired.
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var upper = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlue, Opacity = 0.5f });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        using var small = provider.CreateTarget(new SurfaceSpec(8, 4));
        using var large = provider.CreateTarget(new SurfaceSpec(16, 8));

        scene.Render(small);

        Assert.AreEqual(3, Stats(scene).TargetAcquisitionCount, "Two layer targets and one accumulation surface.");
        Assert.AreEqual(2, Stats(scene).LayerTargetCount);
        Assert.AreEqual(2L * 8L * 4L * 4L, Stats(scene).LayerTargetBytes);

        scene.Render(small);
        scene.Render(small);

        Assert.AreEqual(0, Stats(scene).TargetAcquisitionCount, "A stable frame must reuse every surface.");
        Assert.AreEqual(2L * 8L * 4L * 4L, Stats(scene).LayerTargetBytes);

        scene.Render(large);

        Assert.AreEqual(3, Stats(scene).TargetAcquisitionCount, "A render-scale change must re-acquire.");
        Assert.AreEqual(2L * 16L * 8L * 4L, Stats(scene).LayerTargetBytes);

        scene.Render(large);

        Assert.AreEqual(0, Stats(scene).TargetAcquisitionCount);

        scene.Layers.Remove(upper);
        scene.Render(large);

        Assert.AreEqual(1, Stats(scene).LayerTargetCount, "A removed layer's target must be released.");
        Assert.AreEqual(16L * 8L * 4L, Stats(scene).LayerTargetBytes);
    }

    [TestMethod]
    public void WarmCompositedRenderLoop_AllocatesNoManagedBytes()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var backdrop = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var parent = backdrop.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueRed));
        parent.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueGreen) { ZIndex = 2 });
        var faded = scene.Layers.Add(new ScreenLayer { Opacity = 0.5f, Blend = BlendMode.Screen });
        faded.Root.Add(new RectDrawable(new Rect(2f, 1f, 3f, 2f), OpaqueBlue));
        var framed = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(5f, 0f, 3f, 4f),
        });
        framed.Root.Add(new RectDrawable(new Rect(6f, 1f, 1f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));

        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.Render(target);
        }

        // The probe collects deliberately. A collection retires the runtime's weakly held caches —
        // the reflection metadata behind an enum validation, a backend's managed wrapper for a
        // native object — so the frame straight after one is the frame that reveals a per-frame
        // allocation hiding behind a cache hit. It then settles before the baseline, so that
        // repopulation is not itself mistaken for per-frame cost.
        AllocationProbe.AssertNoneAllocated(
            2_000,
            () => scene.Render(target),
            "The warm composited render loop");

        // Per-frame counts, so they are unaffected by however many frames the probe ran.
        Assert.AreEqual(3, Stats(scene).MergeCount);
        Assert.AreEqual(0, Stats(scene).TargetAcquisitionCount);
    }

    [TestMethod]
    public void TheSceneAcquiresItsCompositorAtLoadAndDisposesItAtUnload()
    {
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var compositor = Compositor(scene);
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(Repeat(Red, 8), Hex(target));

        scene.Unload();

        Assert.IsNull(scene.Compositor, "Unload must release the compositor.");
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => compositor.Render(scene.Layers, target, new Vector2(4f, 2f), 1f),
            "The scene owns the compositor's lifetime, and unload ends it.");
    }

    private static ICompositor Compositor(Scene scene) =>
        scene.Compositor ?? throw new InvalidOperationException("The scene has no compositor.");

    private static CompositorStats Stats(Scene scene) => Compositor(scene).Stats;

    private static string Repeat(string pixel, int count) => string.Concat(Enumerable.Repeat(pixel, count));

    private static string Hex(IRenderTarget target) => Convert.ToHexString(ReadRgba(target)).ToLowerInvariant();

    private static byte[] ReadRgba(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[checked(target.Size.Width * target.Size.Height * 4)];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    /// <summary>Fills one axis-aligned local rectangle with a solid, aliased color.</summary>
    private sealed class RectDrawable : Drawable
    {
        private readonly PathBuilder path;
        private readonly Paint paint;
        private readonly Rect bounds;

        internal RectDrawable(Rect bounds, Color color)
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
