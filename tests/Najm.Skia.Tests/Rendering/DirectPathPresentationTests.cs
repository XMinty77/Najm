using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Pixel proof that <see cref="Scene.RenderDirect(IDrawContext2D)"/> applies the same layer
/// presentation <see cref="Scene.Render(IRenderTarget)"/> composites: clear, viewport, opacity, and
/// blend. The shared <see cref="RenderTraverser"/> exists so the two paths cannot drift, and the
/// assertion that matters here is equality <em>between</em> them — a direct-path client such as a
/// vector exporter has no compositor to fall back on. Every hard-coded expectation is derived from
/// the compositing arithmetic in the comment above it, never captured from a run.
/// </summary>
[TestClass]
public sealed class DirectPathPresentationTests
{
    private const string Red = "ff0000ff";
    private const string Green = "00ff00ff";
    private const string Blue = "0000ffff";

    /// <summary>0.2 green over 0.8 of an opaque-red backdrop: (0,255,0)·0.2 + (255,0,0)·0.8.</summary>
    private const string GreenOverRedAtOneFifth = "cc3300ff";

    /// <summary>(255,128,0) × (128,255,255), per channel, normalized.</summary>
    private const string OrangeTimesCyan = "808000ff";

    /// <summary>(255,0,0) screened with (0,128,0): s + d − s·d, per channel.</summary>
    private const string RedScreenedWithHalfGreen = "ff8000ff";

    /// <summary>128/255 exactly, so a float channel round-trips to the byte 128 with no ambiguity.</summary>
    private const float HalfChannel = 128f / 255f;

    private static readonly Color OpaqueRed = Color.Srgb(1f, 0f, 0f);
    private static readonly Color OpaqueGreen = Color.Srgb(0f, 1f, 0f);
    private static readonly Color OpaqueBlue = Color.Srgb(0f, 0f, 1f);

    [TestMethod]
    public void LayerOpacity_OnTheDirectPathIsPixelIdenticalToTheCompositedPath()
    {
        // 4×2 virtual space on a 4×2 target: renderScale 1, virtual units are pixels. An opaque-red
        // backdrop under a layer that paints opaque green over columns 0-1 at half opacity. Half of
        // 255 is not an eight-bit integer, so the pixels are not derivable here without asserting
        // Skia's rounding; what is derivable — and what the deviation is about — is that both paths
        // must produce the same frame. Dropping the layer's opacity on the direct path, which is
        // what it used to do, makes columns 0-1 fully green and fails this outright.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var fading = scene.Layers.Add(new ScreenLayer { Opacity = 0.5f });
        fading.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var half = RenderBothWays(scene, provider, 4, 2);

        CollectionAssert.AreEqual(
            half.Composited,
            half.Direct,
            "A half-opacity layer must reach the frame the same way on both paths.");
        Assert.AreNotEqual(
            Repeat(Green, 2) + Repeat(Red, 2) + Repeat(Green, 2) + Repeat(Red, 2),
            Hex(half.Direct),
            "Rendering the layer fully opaque is the defect this test exists to catch.");
        Assert.AreNotEqual(Repeat(Red, 8), Hex(half.Direct), "The layer must still contribute.");

        // At one fifth the arithmetic is exact in eight bits, so the frame can be derived outright.
        // Premultiplied source-over with a uniform group alpha of 0.2 and an opaque source:
        //   out = src·0.2 + dst·(1 − 1·0.2) = (0,255,0)·0.2 + (255,0,0)·0.8 = (51 green, 204 red),
        // and 51 and 204 are exact fifths of 255. Where the layer is transparent its premultiplied
        // source is zero and the backdrop survives untouched.
        const string ExpectedRow = GreenOverRedAtOneFifth + GreenOverRedAtOneFifth + Red + Red;

        fading.Opacity = 0.2f;
        var fifth = RenderBothWays(scene, provider, 4, 2);

        Assert.AreEqual(ExpectedRow + ExpectedRow, Hex(fifth.Direct));
        CollectionAssert.AreEqual(fifth.Composited, fifth.Direct);
    }

    [TestMethod]
    public void LayerBlendMultiply_OnTheDirectPathIsPixelIdenticalToTheCompositedPath()
    {
        // Backdrop (255,128,0) under an opaque (128,255,255) layer blended Multiply. Both are
        // opaque, so premultiplied multiply reduces to the per-channel product:
        //   R = 255·(128/255) = 128, G = 128·(255/255) = 128, B = 0·1 = 0, A = 1.
        // Each product keeps one operand at 0 or 255, so every channel is exact in eight bits.
        // Dropping the blend would leave the upper layer's own (128,255,255) instead.
        var scene = new Scene { VirtualResolution = new Vector2(2f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(1f, HalfChannel, 0f) });
        scene.Layers.Add(new ScreenLayer
        {
            ClearColor = Color.Srgb(HalfChannel, 1f, 1f),
            Blend = BlendMode.Multiply,
        });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 2, 1);

        Assert.AreEqual(OrangeTimesCyan + OrangeTimesCyan, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void LayerBlendScreen_OnTheDirectPathIsPixelIdenticalToTheCompositedPath()
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
        var frames = RenderBothWays(scene, provider, 2, 1);

        Assert.AreEqual(RedScreenedWithHalfGreen + RedScreenedWithHalfGreen, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void AViewportdLayer_ClipsToItsRectOnTheDirectPathExactlyAsItIsPlacedOnTheComposited()
    {
        // 8×4 virtual space at renderScale 1 over an opaque-red frame. The upper layer occupies the
        // virtual rect (2,1)-(5,3). The compositor stages it through a 3×2 surface at device (2,1);
        // the direct path clips to that same integer rectangle. Either way its blue clear fills
        // exactly x ∈ [2,5), y ∈ [1,3); its unit square at (2,1) lands on the viewport's own first
        // pixel; and its unit square at (6,3) is outside the viewport and cannot reach the frame.
        // Without the clip that second square would paint a green pixel at (6,3).
        var expectedRows =
            Repeat(Red, 8) +
            Red + Red + Green + Blue + Blue + Red + Red + Red +
            Red + Red + Blue + Blue + Blue + Red + Red + Red +
            Repeat(Red, 8);

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
        var frames = RenderBothWays(scene, provider, 8, 4);

        Assert.AreEqual(expectedRows, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void AViewportdWorldLayer_FramesItsViewportOnTheDirectPathToo()
    {
        // The camera case, where the viewport reframes rather than crops. 8×4 virtual space at
        // renderScale 1 over an opaque-red frame; the upper layer is a WorldLayer2D occupying the
        // virtual rect (4,0)-(8,4), so its camera frames a 4×4 extent whose centre is viewport-local
        // (2,2) — frame virtual (6,2). Its 2×2 world square spans world (-1,-1)-(1,1), which is one
        // virtual unit per world unit with +Y toward the top, so frame pixels x ∈ [5,7), y ∈ [1,3).
        // The blue clear fills the whole viewport, x ∈ [4,8).
        var expectedRows =
            Repeat(Red, 4) + Repeat(Blue, 4) +
            Repeat(Red, 4) + Blue + Green + Green + Blue +
            Repeat(Red, 4) + Blue + Green + Green + Blue +
            Repeat(Red, 4) + Repeat(Blue, 4);

        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var framed = scene.Layers.Add(new WorldLayer2D
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(4f, 0f, 4f, 4f),
        });
        framed.Root.Add(new RectDrawable(new Rect(-1f, -1f, 2f, 2f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 8, 4);

        Assert.AreEqual(expectedRows, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void ALayersClearColorIsContentOnTheDirectPath_IncludingAnUpperClearThatCoversTheFrame()
    {
        // The semantics deviation 10 recorded the old fallback getting wrong: a layer's ClearColor
        // is content, so an upper layer whose subtree draws nothing still covers everything beneath
        // it with an opaque clear. Reading the bottom layer's clear as "the frame background" and
        // ignoring the upper one — what the deleted fallback did — leaves this frame red.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var covering = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlue });

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var covered = RenderBothWays(scene, provider, 4, 2);

        Assert.AreEqual(Repeat(Blue, 8), Hex(covered.Direct));
        CollectionAssert.AreEqual(covered.Composited, covered.Direct);

        // A transparent clear is the identity over the backdrop, not a hole in it.
        covering.ClearColor = Color.Transparent;
        var uncovered = RenderBothWays(scene, provider, 4, 2);

        Assert.AreEqual(Repeat(Red, 8), Hex(uncovered.Direct));
        CollectionAssert.AreEqual(uncovered.Composited, uncovered.Direct);

        // And a layer that cannot contribute contributes no clear either: the upper layer's opaque
        // blue would cover the frame if it were bracketed at all.
        covering.ClearColor = OpaqueBlue;
        covering.Visible = false;
        var hidden = RenderBothWays(scene, provider, 4, 2);

        Assert.AreEqual(Repeat(Red, 8), Hex(hidden.Direct));
        CollectionAssert.AreEqual(hidden.Composited, hidden.Direct);

        covering.Visible = true;
        covering.Opacity = 0f;
        var transparentLayer = RenderBothWays(scene, provider, 4, 2);

        Assert.AreEqual(Repeat(Red, 8), Hex(transparentLayer.Direct));
        CollectionAssert.AreEqual(transparentLayer.Composited, transparentLayer.Direct);
    }

    [TestMethod]
    public void AWholeSceneOfMixedPresentation_RendersIdenticallyOnBothPaths()
    {
        // Every presentation dimension at once, over a ticked frame: an opaque backdrop, a world
        // layer at half opacity with a non-default blend, and a viewport'd overlay at quarter
        // opacity. No pixel is hard-coded — the claim under test is that one traverser drives both
        // paths to the same frame, which is the entire reason the traverser is shared.
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var backdrop = scene.Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(0f, 0f, 0f) });
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
        var frames = RenderBothWays(scene, provider, 8, 4);

        CollectionAssert.AreEqual(frames.Composited, frames.Direct);

        // Rendering one ticked frame twice down the direct path must also be byte identical, since
        // the brackets are opened and closed from scene state the walk never mutates.
        var again = RenderBothWays(scene, provider, 8, 4);

        CollectionAssert.AreEqual(frames.Direct, again.Direct);
    }

    [TestMethod]
    public void AtTwiceTheRenderScale_TheDirectPathStillMatchesTheCompositedPath()
    {
        // 4×2 virtual space on an 8×4 target: renderScale min(8/4, 4/2) = 2, so every virtual unit
        // is a 2×2 block of pixels and the viewport (1,0)-(3,2) becomes the device rect (2,0)-(6,4).
        // A render scale the bracket ignored would clip the overlay to a quarter of its rect.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var overlay = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(1f, 0f, 2f, 2f),
        });
        overlay.Root.Add(new RectDrawable(new Rect(1f, 0f, 1f, 1f), OpaqueGreen));

        // Rows of 8 device pixels: the blue viewport covers device columns 2-5 in every row, and
        // the green unit square covers device columns 2-3 of rows 0-1.
        var expectedRows =
            Repeat(Red, 2) + Repeat(Green, 2) + Repeat(Blue, 2) + Repeat(Red, 2) +
            Repeat(Red, 2) + Repeat(Green, 2) + Repeat(Blue, 2) + Repeat(Red, 2) +
            Repeat(Red, 2) + Repeat(Blue, 4) + Repeat(Red, 2) +
            Repeat(Red, 2) + Repeat(Blue, 4) + Repeat(Red, 2);

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 8, 4);

        Assert.AreEqual(expectedRows, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void ANonDefaultNodeBlend_SeesItsOwnLayerOnBothPaths_NotTheFrameBeneathIt()
    {
        // The bracket isolates its layer as a group even when the group composite would be an
        // identity, and this pins why. The upper layer is opacity one with the default blend, so it
        // is exactly the layer a direct-path bracket skip (FP-5) would flatten — but it contains a
        // node painted with Multiply, and a node's blend reaches only as far as its own layer.
        //
        // Composited: the node multiplies against the layer's transparent staging surface, and
        // separable multiply with a zero-alpha backdrop is (1−αs)·d + (1−αd)·s + s·d = s, so the
        // staged layer holds the node's own (128,255,255) and merges over the backdrop opaquely.
        // Flattening the layer away would instead multiply the node against the frame's (255,128,0)
        // and give (128,128,0) — a visibly different frame, arrived at silently. Skipping the group
        // needs the subtree predicate that decides when it is safe, and that is M2.
        const string NodesOwnColor = "80ffffff";

        var scene = new Scene { VirtualResolution = new Vector2(2f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(1f, HalfChannel, 0f) });
        var upper = scene.Layers.Add(new ScreenLayer());
        upper.Root.Add(new RectDrawable(
            new Rect(0f, 0f, 2f, 1f),
            Color.Srgb(HalfChannel, 1f, 1f),
            BlendMode.Multiply));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 2, 1);

        Assert.AreEqual(NodesOwnColor + NodesOwnColor, Hex(frames.Direct));
        Assert.AreNotEqual(
            OrangeTimesCyan + OrangeTimesCyan,
            Hex(frames.Direct),
            "Multiplying against the frame instead of the layer is the drift a bracket skip would introduce.");
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void SetEngineTransform_StillRejectsAnAuthorPushWhileAnEngineBracketIsOpen()
    {
        // The two depths are counted apart. An open engine bracket must not make the engine
        // transform illegal — the traverser sets one per node inside every bracket — while an
        // outstanding author push must still make it illegal, which is what lets the traverser
        // treat "authors balance their pushes within Render" as a structural guarantee.
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(4, 2));
        var context = target.GetContext();

        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 0.5f, BlendMode.Multiply, null));
        context.SetEngineTransform(Matrix3x2.CreateTranslation(1f, 0f));
        context.SetEngineTransform(Matrix3x2.CreateScale(2f));

        context.PushTransform(Matrix3x2.CreateTranslation(1f, 0f));
        var rejected = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.SetEngineTransform(Matrix3x2.Identity));
        StringAssert.Contains(rejected.Message, "1 unbalanced context state push(es)");
        StringAssert.Contains(rejected.Message, "(transform)");

        // The same rule guards both ends of the bracket, so an unbalanced author push cannot be
        // swallowed by closing the group over the top of it.
        var refusedClose = Assert.ThrowsExactly<InvalidOperationException>(context.EndLayerBracket);
        StringAssert.Contains(refusedClose.Message, "1 unbalanced context state push(es)");
        var refusedOpen = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.BeginLayerBracket(default));
        StringAssert.Contains(refusedOpen.Message, "1 unbalanced context state push(es)");

        context.PopTransform();
        context.SetEngineTransform(Matrix3x2.Identity);
        context.EndLayerBracket();
        target.EndPass();
    }

    [TestMethod]
    public void AnUnbalancedEngineBracket_IsReportedWhenThePassEnds()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(4, 2));
        var context = target.GetContext();

        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.BeginLayerBracket(new LayerBracket(OpaqueBlue, 1f, BlendMode.SrcOver, null));

        var brackets = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
        StringAssert.Contains(brackets.Message, "2 unbalanced engine layer bracket(s)");
        StringAssert.Contains(brackets.Message, "baseline state was restored");

        // Restored means restored: the next pass starts clean, and the report names both kinds when
        // both are outstanding.
        context = target.GetContext();
        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.PushOpacity(0.5f);

        var both = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
        StringAssert.Contains(both.Message, "1 unbalanced context state push(es)");
        StringAssert.Contains(both.Message, "1 unbalanced engine layer bracket(s)");

        context = target.GetContext();
        var unopened = Assert.ThrowsExactly<InvalidOperationException>(context.EndLayerBracket);
        StringAssert.Contains(unopened.Message, "no engine layer bracket is open");
        target.EndPass();
    }

    [TestMethod]
    public void ABracketRejectsABlendModeTheBackendCannotLower_WithoutOpeningAnything()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 1));
        var context = target.GetContext();

        var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, (BlendMode)9999, null)));
        Assert.AreEqual("blendMode", rejected.ParamName);

        // Nothing was opened, so the pass ends clean rather than reporting a bracket the caller
        // never got.
        target.EndPass();
    }

    [TestMethod]
    public void AWarmMultiLayerRenderDirectLoop_AllocatesNoManagedBytes()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var backdrop = scene.Layers.Add(new ScreenLayer { ClearColor = Color.Srgb(0f, 0f, 0f) });
        var parent = backdrop.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueRed));
        var child = parent.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueGreen) { ZIndex = 2 });
        var faded = scene.Layers.Add(new ScreenLayer { Opacity = 0.5f, Blend = BlendMode.Screen });
        var fadedRect = faded.Root.Add(new RectDrawable(new Rect(2f, 1f, 3f, 2f), OpaqueBlue));
        var framed = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(5f, 0f, 3f, 4f),
        });
        var framedRect = framed.Root.Add(new RectDrawable(new Rect(6f, 1f, 1f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));

        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(8, 4));
        var context = target.GetContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        // Three brackets a frame, each of them a clip, a group layer, and a clear fill. None of that
        // may cost the managed heap anything: the bracket is a struct passed by reference, its
        // native paint is the context's own, and its save-slot bookkeeping is arithmetic.
        var reading = AllocationProbe.AssertNoneAllocated(
            2_000,
            () => scene.RenderDirect(context),
            "The warm direct-path render loop");

        // Four drawables, every frame. The probe owns the frame count, so the expected total is
        // derived from it rather than fixed.
        Assert.AreEqual(
            (64 + reading.Invocations) * 4,
            parent.Draws + child.Draws + fadedRect.Draws + framedRect.Draws);
        target.EndPass();
    }

    /// <summary>
    /// Renders one loaded scene both ways into fresh targets of the same size and returns both
    /// frames.
    /// </summary>
    /// <remarks>
    /// The direct target is cleared to transparent first because the direct path never clears the
    /// surface — that is the caller's, and here the composited path's accumulation surface starts
    /// transparent too, which is what makes the two comparable. Ending the pass afterwards is itself
    /// an assertion: an unclosed bracket or author push would throw here.
    /// </remarks>
    private static (byte[] Composited, byte[] Direct) RenderBothWays(
        Scene scene,
        RasterSkiaSurfaceProvider provider,
        int width,
        int height)
    {
        var renderScale = MathF.Min(
            width / scene.VirtualResolution.X,
            height / scene.VirtualResolution.Y);

        using var composited = provider.CreateTarget(new SurfaceSpec(width, height));
        using var direct = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(width, height));

        scene.Render(composited);

        var context = direct.GetContext(renderScale);
        context.Clear(Color.Transparent);
        scene.RenderDirect(context);
        direct.EndPass();

        return (ReadRgba(composited), ReadRgba(direct));
    }

    private static string Repeat(string pixel, int count) => string.Concat(Enumerable.Repeat(pixel, count));

    private static string Hex(byte[] pixels) => Convert.ToHexString(pixels).ToLowerInvariant();

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

        internal RectDrawable(Rect bounds, Color color, BlendMode blend = BlendMode.SrcOver)
        {
            this.bounds = bounds;
            paint = Paint.Fill(color, isAntialias: false, blendMode: blend);
            path = new PathBuilder(initialCapacity: 5)
                .MoveTo(bounds.X, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y + bounds.Height)
                .LineTo(bounds.X, bounds.Y + bounds.Height)
                .Close();
        }

        /// <summary>Gets how many times this drawable has painted, for the allocation probe.</summary>
        internal int Draws { get; private set; }

        public override Rect GeometryBounds => bounds;

        public override void Render(IDrawContext2D context)
        {
            Draws++;
            context.DrawPath(path, paint);
        }
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
