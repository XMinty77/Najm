using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Pixel proof for §6.7's node-tier composition unit as M1 implements it:
/// <see cref="Node2D.Opacity"/>, <see cref="Node2D.Blend"/>, and <see cref="Node2D.Isolate"/>.
/// </summary>
/// <remarks>
/// <para>
/// The claim that actually needs pixels is <em>group</em> semantics. A per-element reading of
/// opacity — attenuate each drawn thing on its way to the surface — agrees with the group reading
/// everywhere except where content overlaps, and disagrees visibly there. Every opacity test below
/// therefore puts two overlapping children under one faded parent and reads the overlap.
/// </para>
/// <para>
/// The second claim is cross-path equality, per deviation 11: the shared
/// <see cref="RenderTraverser"/> opens these brackets, so a composited frame and a direct frame of
/// the same scene must be byte identical. A direct-path client such as a vector exporter has no
/// compositor to fall back on.
/// </para>
/// <para>
/// Every hard-coded expectation is derived from the compositing arithmetic in the comment above it,
/// never captured from a run.
/// </para>
/// </remarks>
[TestClass]
public sealed class NodeCompositionTests
{
    private const string Black = "000000ff";
    private const string Red = "ff0000ff";
    private const string Green = "00ff00ff";

    /// <summary>128/255 exactly, so a float channel round-trips to the byte 128 with no ambiguity.</summary>
    private const float HalfChannel = 128f / 255f;

    private static readonly Color OpaqueBlack = Color.Srgb(0f, 0f, 0f);
    private static readonly Color OpaqueRed = Color.Srgb(1f, 0f, 0f);
    private static readonly Color OpaqueGreen = Color.Srgb(0f, 1f, 0f);
    private static readonly Color OpaqueBlue = Color.Srgb(0f, 0f, 1f);
    private static readonly Color OpaqueOrange = Color.Srgb(1f, HalfChannel, 0f);
    private static readonly Color OpaqueCyan = Color.Srgb(HalfChannel, 1f, 1f);

    [TestMethod]
    public void GroupOpacityCompositesTheWholeSubtreeOnce_NotEachChildSeparately()
    {
        // 4×1 virtual on a 4×1 target: renderScale 1, virtual units are pixels. An opaque-black
        // backdrop under a layer holding one group node at opacity 0.2 with two overlapping opaque
        // children: red over x ∈ [0,2), then green over x ∈ [1,3).
        //
        // One fifth is used rather than one half because 51 and 204 are exact fifths of 255, so the
        // whole frame is derivable in eight bits with no appeal to Skia's rounding.
        //
        // GROUP semantics — the children composite among themselves at full alpha first, and the
        // result is attenuated once:
        //   group content: x=0 red(255,0,0); x∈{1,2} green(0,255,0) (green painted last wins);
        //                  x=3 nothing.
        //   over black at 0.2: out = 0.2·src + 0.8·(0,0,0), so 0.2·255 = 51 = 0x33.
        //   ⇒ 330000, 003300, 003300, 000000.
        //
        // PER-ELEMENT semantics — each child attenuated on its own way to the surface — agrees
        // everywhere the children do not overlap, and the children overlap on exactly one pixel,
        // x=1. There the red lands premultiplied (51,0,0) at α=0.2 and the green lands over it at
        // α=0.2, giving (0,51,0) + 0.8·(51,0,0) = (41,51,0) at α=0.36; over black that is 0x29 red,
        // not 0. The red channel of that one pixel is therefore the whole difference between the two
        // readings, and it is 0 for exactly one of them. (Mutation-checked: bracketing each child
        // separately instead of the subtree yields 330000ff 293300ff 003300ff 000000ff.)
        const string RedAtOneFifth = "330000ff";
        const string GreenAtOneFifth = "003300ff";
        var expected = RedAtOneFifth + GreenAtOneFifth + GreenAtOneFifth + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D { Opacity = 0.2f });
        group.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueRed));
        group.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(expected, Hex(frames.Direct));
        CollectionAssert.AreEqual(
            frames.Composited,
            frames.Direct,
            "Node opacity must reach the frame the same way on both paths.");

        // Stated again as the single byte that separates the two readings, so a regression to
        // per-element opacity names itself instead of arriving as a wall of hex.
        Assert.AreEqual(
            0,
            frames.Direct[4],
            "The overlap must carry no red: red belongs to the covered child, and a group composites "
                + "its children among themselves before the group alpha applies.");

        // And the group really is doing something — full opacity is a different frame.
        group.Opacity = 1f;
        var opaque = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Red + Green + Green + Black, Hex(opaque.Direct));
        CollectionAssert.AreEqual(opaque.Composited, opaque.Direct);
    }

    [TestMethod]
    public void GroupOpacityAppliesToDescendantsAtEveryDepth()
    {
        // The same fifth and the same expected frame, but the overlap now crosses a depth: the red
        // rectangle is the group's own child and the green one is a grandchild behind an
        // intermediate node with no composition state. The unit is the whole subtree, so where they
        // overlap they must still composite among themselves before the group alpha applies.
        const string RedAtOneFifth = "330000ff";
        const string GreenAtOneFifth = "003300ff";
        var expected = RedAtOneFifth + GreenAtOneFifth + GreenAtOneFifth + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D { Opacity = 0.2f });
        group.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueRed));
        var passthrough = group.Add(new Node2D());
        passthrough.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(expected, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
    }

    [TestMethod]
    public void ANodeBlendCompositesAgainstItsOwnLayer_IdenticallyOnBothPaths()
    {
        // 4×1 virtual at renderScale 1. An opaque-red backdrop under a layer with a transparent
        // clear holding two siblings: an opaque orange (255,128,0) over x ∈ [0,2) with default
        // composition, and an opaque cyan (128,255,255) over x ∈ [1,3) whose *node* blend is
        // Multiply. Every product below keeps one operand at 0 or 255, so all of it is exact.
        //
        //   x=0 — orange only, untouched by the unit: (255,128,0).
        //   x=1 — the unit multiplies against the layer's own orange, both opaque, so the result is
        //         the per-channel product: (255·128/255, 128·255/255, 0·255/255) = (128,128,0).
        //   x=2 — the unit multiplies against the layer's *transparent* pixel. Separable multiply is
        //         (1−αs)·d + (1−αd)·s + s·d, and with d = 0 and αd = 0 that is s: (128,255,255).
        //   x=3 — the layer contributes nothing, so the red backdrop survives.
        //
        // The x=2 column is the load-bearing one: a node blend reaches only as far as its own layer,
        // so it must see transparency there rather than the red frame beneath. Multiplying against
        // the frame would give (255·128/255, 0, 0) = (128,0,0) instead.
        const string Orange = "ff8000ff";
        const string OrangeTimesCyan = "808000ff";
        const string Cyan = "80ffffff";

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var layer = scene.Layers.Add(new ScreenLayer());
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueOrange));
        var multiplied = layer.Root.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueCyan));
        multiplied.Blend = BlendMode.Multiply;

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Orange + OrangeTimesCyan + Cyan + Red, Hex(frames.Direct));
        Assert.AreNotEqual(
            "800000ff",
            Hex(frames.Direct)[16..24],
            "A node blend that reached past its layer to the frame is the drift this pins.");
        CollectionAssert.AreEqual(
            frames.Composited,
            frames.Direct,
            "Node blend must reach the frame the same way on both paths.");
    }

    [TestMethod]
    public void AnIsolatingAncestorGivesADescendantBlendItsOwnScope()
    {
        // What Isolate is for. Same two siblings as the blend test, but wrapped in a parent that
        // isolates: the multiply now composites against the parent's own unit, which starts
        // transparent and holds only the orange. Inside the unit the arithmetic is unchanged
        // ((255,128,0) then (128,128,0) then (128,255,255)), and the unit merges over the red
        // backdrop opaquely — so the frame equals the unisolated one. That equality is the point:
        // the isolating parent has moved the blend's scope without changing its result, because
        // nothing outside the unit was ever in that scope.
        //
        // With Isolate left off, the same tree is drawn without the extra group, so byte equality
        // between the two runs is the assertion that Isolate costs a bracket and nothing else here.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueRed });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D());
        group.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueOrange));
        var multiplied = group.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueCyan));
        multiplied.Blend = BlendMode.Multiply;

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var unisolated = RenderBothWays(scene, provider, 4, 1);

        group.Isolate = true;
        var isolated = RenderBothWays(scene, provider, 4, 1);

        CollectionAssert.AreEqual(isolated.Composited, isolated.Direct);
        CollectionAssert.AreEqual(
            unisolated.Direct,
            isolated.Direct,
            "An isolating scope that contains the whole blend must not change its result.");

        // Now give the isolating scope something the blend could otherwise have reached: an opaque
        // green sibling *outside* the group, painted first. Unisolated, the multiply sees it; with
        // the group isolating, it does not, and the frames must differ.
        group.Isolate = false;
        var beneath = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 4f, 1f), OpaqueGreen));
        beneath.ZIndex = -1;
        var exposed = RenderBothWays(scene, provider, 4, 1);

        group.Isolate = true;
        var shielded = RenderBothWays(scene, provider, 4, 1);

        CollectionAssert.AreEqual(exposed.Composited, exposed.Direct);
        CollectionAssert.AreEqual(shielded.Composited, shielded.Direct);
        CollectionAssert.AreNotEqual(
            exposed.Direct,
            shielded.Direct,
            "Isolate must stop a descendant blend from reaching content outside the scope.");
    }

    [TestMethod]
    public void AWholeSceneOfNodeCompositionRendersIdenticallyOnBothPaths()
    {
        // Every node-tier dimension at once, nested, inside layers that carry their own presentation,
        // at twice the render scale, over a ticked frame. No pixel is hard-coded — the claim is that
        // one traverser drives both paths to the same frame, which is the entire reason it is shared.
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var backdrop = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        backdrop.Root.Add(new RectDrawable(new Rect(0f, 0f, 5f, 3f), OpaqueRed));

        var world = scene.Layers.Add(new WorldLayer2D { Opacity = 0.75f, Blend = BlendMode.Screen });
        var faded = world.Root.Add(new Node2D { Opacity = 0.4f });
        faded.Position = new Vector2(-1f, 0f);
        faded.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));
        var inner = faded.Add(new RectDrawable(new Rect(1f, 1f, 2f, 2f), OpaqueBlue));
        inner.Blend = BlendMode.Multiply;

        var overlay = scene.Layers.Add(new ScreenLayer
        {
            ClearColor = OpaqueBlue,
            Viewport = new Rect(5f, 0f, 3f, 4f),
            Opacity = 0.5f,
        });
        var isolated = overlay.Root.Add(new Node2D { Isolate = true });
        isolated.Add(new RectDrawable(new Rect(5f, 1f, 2f, 1f), OpaqueGreen));
        var overlapping = isolated.Add(new RectDrawable(new Rect(6f, 1f, 2f, 2f), OpaqueOrange));
        overlapping.Opacity = 0.5f;

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));
        var frames = RenderBothWays(scene, provider, 16, 8);

        CollectionAssert.AreEqual(frames.Composited, frames.Direct);

        // Rendering one ticked frame twice must also be byte identical, since the brackets are
        // opened and closed from scene state the walk never mutates.
        var again = RenderBothWays(scene, provider, 16, 8);

        CollectionAssert.AreEqual(frames.Direct, again.Direct);
        CollectionAssert.AreEqual(frames.Composited, again.Composited);
    }

    [TestMethod]
    public void ADefaultNodeOpensNoUnitBracketAtAll()
    {
        // The zero-cost fast path, observed where it can be observed: a scene of default nodes must
        // produce the byte-identical frame it produced before node composition existed, and the
        // context must never have been asked to open a unit. The proxy for "never asked" is that
        // making the context refuse unit brackets outright changes nothing.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D());
        group.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueRed));
        group.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Red + Green + Green + Black, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);

        var counting = new UnitCountingContext();
        scene.RenderDirect(counting);

        Assert.AreEqual(0, counting.UnitBracketCount, "A tree of default nodes must open no units.");
    }

    [TestMethod]
    public void AWarmDirectLoopOverIsolatingNodesAllocatesNoManagedBytes()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var group = layer.Root.Add(new Node2D { Opacity = 0.5f });
        var first = group.Add(new RectDrawable(new Rect(0f, 0f, 4f, 3f), OpaqueRed));
        var second = group.Add(new RectDrawable(new Rect(2f, 1f, 4f, 3f), OpaqueGreen));
        second.Blend = BlendMode.Screen;
        var forced = layer.Root.Add(new RectDrawable(new Rect(5f, 0f, 2f, 2f), OpaqueBlue));
        forced.Isolate = true;

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));

        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(8, 4));
        var context = target.GetContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        // Three unit brackets a frame, each a native SaveLayer and its restore. None of that may
        // cost the managed heap anything: the bracket is a struct passed by reference, its paint is
        // the context's own, and its save-slot bookkeeping is a byte written to a preallocated stack.
        var reading = AllocationProbe.AssertNoneAllocated(
            2_000,
            () => scene.RenderDirect(context),
            "The warm direct-path render loop over isolating nodes");

        Assert.AreEqual((64 + reading.Invocations) * 3, first.Draws + second.Draws + forced.Draws);
        target.EndPass();
    }

    [TestMethod]
    public void SetEngineTransformToleratesAnOpenUnitBracketButStillRefusesAnAuthorPush()
    {
        // Exactly the rule the layer bracket already lives by, extended to the node bracket, and for
        // the same reason: the traverser sets one engine transform per node inside every open unit,
        // so an open unit must not make the transform illegal — while an outstanding author push
        // still must.
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(4, 2));
        var context = target.GetContext();

        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.SetEngineTransform(Matrix3x2.CreateTranslation(1f, 0f));
        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.Multiply));
        context.SetEngineTransform(Matrix3x2.CreateScale(2f));
        context.SetEngineTransform(Matrix3x2.Identity);

        context.PushOpacity(0.5f);
        var rejected = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.SetEngineTransform(Matrix3x2.Identity));
        StringAssert.Contains(rejected.Message, "1 unbalanced context state push(es)");

        var refusedClose = Assert.ThrowsExactly<InvalidOperationException>(context.EndUnitBracket);
        StringAssert.Contains(refusedClose.Message, "1 unbalanced context state push(es)");
        var refusedOpen = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.BeginUnitBracket(default));
        StringAssert.Contains(refusedOpen.Message, "1 unbalanced context state push(es)");

        context.PopOpacity();
        context.EndUnitBracket();
        context.EndLayerBracket();
        target.EndPass();
    }

    [TestMethod]
    public void EngineBracketsOfBothKindsMustCloseInTheOrderTheyWereOpened()
    {
        // They share the canvas save stack, so closing them out of order would restore another
        // bracket's slots. The kind check makes that a named error instead of a silently wrong frame.
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(4, 2));
        var context = target.GetContext();

        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.SrcOver));

        var crossed = Assert.ThrowsExactly<InvalidOperationException>(context.EndLayerBracket);
        StringAssert.Contains(crossed.Message, "more recently opened engine unit bracket");

        // The rule is symmetric: with only the layer bracket left, a unit close names the layer
        // bracket standing in its way rather than pretending the stack is empty.
        context.EndUnitBracket();
        var alsoCrossed = Assert.ThrowsExactly<InvalidOperationException>(context.EndUnitBracket);
        StringAssert.Contains(alsoCrossed.Message, "more recently opened engine layer bracket");

        context.EndLayerBracket();
        var unopened = Assert.ThrowsExactly<InvalidOperationException>(context.EndUnitBracket);
        StringAssert.Contains(unopened.Message, "no engine unit bracket is open");
        target.EndPass();
    }

    [TestMethod]
    public void AnUnbalancedUnitBracketIsReportedWhenThePassEnds_AlongsideTheOtherKinds()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(4, 2));
        var context = target.GetContext();

        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.SrcOver));
        context.BeginUnitBracket(new UnitBracket(0.25f, BlendMode.SrcOver));

        var units = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
        StringAssert.Contains(units.Message, "2 unbalanced engine unit bracket(s)");
        StringAssert.Contains(units.Message, "baseline state was restored");

        // Restored means restored, and all three kinds are named separately, because they are owned
        // by three different callers and a fix for one is not a fix for another.
        context = target.GetContext();
        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.SrcOver));
        context.PushClip(new Rect(0f, 0f, 1f, 1f));

        var all = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
        StringAssert.Contains(all.Message, "1 unbalanced context state push(es)");
        StringAssert.Contains(all.Message, "1 unbalanced engine layer bracket(s)");
        StringAssert.Contains(all.Message, "1 unbalanced engine unit bracket(s)");

        context = target.GetContext();
        target.EndPass();
    }

    [TestMethod]
    public void AUnitBracketRejectsABlendModeTheBackendCannotLower_WithoutOpeningAnything()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 1));
        var context = target.GetContext();

        var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.BeginUnitBracket(new UnitBracket(1f, (BlendMode)9999)));
        Assert.AreEqual("blendMode", rejected.ParamName);

        // Nothing was opened, so the pass ends clean rather than reporting a bracket the caller
        // never got.
        target.EndPass();
    }

    /// <summary>
    /// Renders one loaded scene both ways into fresh targets of the same size and returns both
    /// frames.
    /// </summary>
    /// <remarks>
    /// The direct target is cleared to transparent first because the direct path never clears the
    /// surface — that is the caller's job, and here the composited path's accumulation surface
    /// starts transparent too, which is what makes the two comparable. Ending the pass afterwards is
    /// itself an assertion: an unclosed bracket or author push would throw here.
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

        /// <summary>Gets how many times this drawable has painted, for the allocation probe.</summary>
        internal int Draws { get; private set; }

        public override Rect GeometryBounds => bounds;

        public override void Render(IDrawContext2D context)
        {
            Draws++;
            context.DrawPath(path, paint);
        }
    }

    /// <summary>Counts the unit brackets a walk asks for, and does nothing else.</summary>
    private sealed class UnitCountingContext : DrawContext2DBase
    {
        internal int UnitBracketCount { get; private set; }

        public override SurfaceSpec SurfaceSpec { get; } = new(4, 1);

        public override RenderCaps Caps => RenderCaps.None;

        public override float RenderScale => 1f;

        public override float Scale => 1f;

        public override void Clear(Color color)
        {
        }

        public override void DrawPath(PathBuilder path, in Paint paint)
        {
        }

        public override void DrawImage(
            IImage image,
            in Matrix3x2 imageToLocal,
            ImageSampling sampling = ImageSampling.Linear)
        {
        }

        public override void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
        }

        public override void BeginLayerBracket(in LayerBracket bracket)
        {
        }

        public override void EndLayerBracket()
        {
        }

        public override void BeginUnitBracket(in UnitBracket bracket) => UnitBracketCount++;

        public override void EndUnitBracket()
        {
        }

        public override void PushTransform(in Matrix3x2 localTransform)
        {
        }

        public override void PopTransform()
        {
        }

        public override void PushClip(in Rect bounds)
        {
        }

        public override void PushClip(PathBuilder path)
        {
        }

        public override void PopClip()
        {
        }

        public override void PushOpacity(float opacity)
        {
        }

        public override void PopOpacity()
        {
        }
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
