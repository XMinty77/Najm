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
        var group = layer.Root.Add(new Node2D { Opacity = 0.5f, Clip = new Rect(0f, 0f, 6f, 3f) });
        var first = group.Add(new RectDrawable(new Rect(0f, 0f, 4f, 3f), OpaqueRed));
        var second = group.Add(new RectDrawable(new Rect(2f, 1f, 4f, 3f), OpaqueGreen));
        second.Blend = BlendMode.Screen;
        var forced = layer.Root.Add(new RectDrawable(new Rect(5f, 0f, 2f, 2f), OpaqueBlue));
        forced.Isolate = true;
        var bounded = layer.Root.Add(new RectDrawable(new Rect(1f, 2f, 3f, 2f), OpaqueCyan));
        bounded.Clip = new Rect(1f, 2f, 2f, 1f);

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        scene.Tick(Ticks.At(0));

        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(8, 4));
        var context = target.GetContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        // Three unit brackets a frame, each a native SaveLayer and its restore, and two clip
        // brackets, each a save and a ClipRect. None of that may cost the managed heap anything:
        // a bracket is a struct passed by reference, its paint is the context's own, its rectangle
        // rides the struct, and its save-slot bookkeeping is a byte written to a preallocated stack.
        // Reading Node2D.Clip on the walk is a nullable field read and must not box either.
        var reading = AllocationProbe.AssertNoneAllocated(
            2_000,
            () => scene.RenderDirect(context),
            "The warm direct-path render loop over isolating and clipped nodes");

        Assert.AreEqual(
            (64 + reading.Invocations) * 4,
            first.Draws + second.Draws + forced.Draws + bounded.Draws);
        target.EndPass();
    }

    [TestMethod]
    public void AClipBoundsTheWholeSubtreeAndNotOnlyTheNodeThatCarriesIt()
    {
        // 4×1 virtual on a 4×1 target: renderScale 1, virtual units are pixels. An opaque-black
        // backdrop under a layer holding one clipped group at x = 1:
        //   group: Position (1,0), Clip = (0,0,2,1) local ⇒ device x ∈ [1,3).
        //   its own paint: opaque red over local (0,0,4,1) ⇒ device x ∈ [1,5), clipped to [1,3).
        //   its child:     opaque green over local (-1,0,1,1) ⇒ device x ∈ [0,1), wholly outside.
        //
        // Both halves of the claim are in this one frame. Pixel 3 is black, so the clip bounded the
        // node's own paint. Pixel 0 is black, so it also bounded a child that never mentioned it —
        // the thing a PushClip inside each leaf's Render cannot do, because a leaf can only clip
        // itself and a leaf that forgets is not clipped at all.
        var expected = Black + Red + Red + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 4f, 1f), OpaqueRed));
        group.Position = new Vector2(1f, 0f);
        group.Clip = new Rect(0f, 0f, 2f, 1f);
        group.Add(new RectDrawable(new Rect(-1f, 0f, 1f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(expected, Hex(frames.Direct));
        CollectionAssert.AreEqual(
            frames.Composited,
            frames.Direct,
            "A node clip must reach the frame the same way on both paths.");

        // Without the clip the same tree is a different frame, so the clip is doing the work and
        // not merely agreeing with geometry that was already inside it.
        group.Clip = null;
        var unclipped = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Green + Red + Red + Red, Hex(unclipped.Direct));
        CollectionAssert.AreEqual(unclipped.Composited, unclipped.Direct);

        // The rectangle is in the node's own local space, so it scales with the node: the same
        // (0,0,2,1) under Scale 2 covers local x ∈ [0,2) ⇒ device x ∈ [1,5), and only the surface
        // ends it. The node's red spans local [0,4) ⇒ device [1,9), so pixels 1..3 are red and the
        // green child, still at device [0,1), is still outside.
        group.Clip = new Rect(0f, 0f, 2f, 1f);
        group.Scale = new Vector2(2f, 1f);
        var scaled = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Black + Red + Red + Red, Hex(scaled.Direct));
        CollectionAssert.AreEqual(scaled.Composited, scaled.Direct);
    }

    [TestMethod]
    public void AClipAndAGroupOpacityApplyToOneAndTheSameUnit()
    {
        // The group-opacity frame from the first test in this class, with a clip added: two
        // overlapping opaque children under a group at opacity 0.2, clipped to device x ∈ [0,2).
        //   group content: x=0 red, x∈{1,2} green (green painted last wins), x=3 nothing.
        //   clip keeps x ∈ {0,1}; over black at 0.2 that is 0.2·255 = 51 = 0x33.
        //   ⇒ 330000, 003300, 000000, 000000.
        //
        // Pixel 1 is the load-bearing one twice over. It is inside the clip, so it survives; and it
        // is the pixel where the two children overlap, so its red channel is 0 only if the children
        // composited among themselves before the group alpha applied. The clip and the opacity are
        // two brackets — the clip bounds, the unit isolates — and this pixel is where a clip that
        // had somehow split the unit in two would show the wrong overlap while still producing the
        // right silhouette.
        const string RedAtOneFifth = "330000ff";
        const string GreenAtOneFifth = "003300ff";
        var expected = RedAtOneFifth + GreenAtOneFifth + Black + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D
        {
            Opacity = 0.2f,
            Clip = new Rect(0f, 0f, 2f, 1f),
        });
        group.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueRed));
        group.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(expected, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
        Assert.AreEqual(
            0,
            frames.Direct[4],
            "The overlap must carry no red: a clipped unit is still one unit, and its children "
                + "composite among themselves before the group alpha applies.");
    }

    [TestMethod]
    public void AClipOnlyAncestorIsNotAStackingScopeForADescendantBlend()
    {
        // §6.7's table, in pixels: "clip state alone does not isolate". This is the frame that tells
        // the two readings apart, and it is the one the isolating-clip implementation got wrong.
        //
        // 4×1 virtual on a 4×1 target: renderScale 1, virtual units are pixels. An opaque-black
        // backdrop layer under a layer with a transparent clear holding, in paint order:
        //   an opaque orange (255,128,0) over x ∈ [0,4);
        //   a group node whose ONLY composition state is Clip = (0,0,3,1) ⇒ device x ∈ [0,3);
        //   under the group, an opaque cyan (128,255,255) over x ∈ [1,4) with Blend = Multiply.
        //
        // CORRECT — the clip bounds the subtree and isolates nothing, so the descendant's multiply
        // composites against what lies beneath the clipping node, which is the orange:
        //   x=0 — no cyan: orange (255,128,0).
        //   x=1 — multiply against opaque orange, per channel: (255·128, 128·255, 0·255)/255
        //         = (128,128,0).
        //   x=2 — the same: (128,128,0).
        //   x=3 — outside the clip, so the cyan never lands: orange (255,128,0).
        //
        // WRONG — the clip opens an isolating unit, so the group is a stacking scope and the
        // multiply sees the unit's transparent interior instead. Separable multiply is
        // (1−αs)·d + (1−αd)·s + s·d, and with d = 0 and αd = 0 that is s, so the unit holds opaque
        // cyan; the unit then merges over the orange source-over at full alpha and the cyan simply
        // wins:
        //   x=0 — orange (255,128,0); x∈{1,2} — cyan (128,255,255); x=3 — orange (255,128,0).
        //
        // The two readings differ on the blue channel of x=1 by the whole range: 0 against 255.
        const string Orange = "ff8000ff";
        const string OrangeTimesCyan = "808000ff";
        const string Cyan = "80ffffff";
        var whenClipBoundsOnly = Orange + OrangeTimesCyan + OrangeTimesCyan + Orange;
        var whenClipIsolates = Orange + Cyan + Cyan + Orange;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 4f, 1f), OpaqueOrange));
        var group = layer.Root.Add(new Node2D { Clip = new Rect(0f, 0f, 3f, 1f) });
        var multiplied = group.Add(new RectDrawable(new Rect(1f, 0f, 3f, 1f), OpaqueCyan));
        multiplied.Blend = BlendMode.Multiply;

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(whenClipBoundsOnly, Hex(frames.Direct));
        Assert.AreNotEqual(whenClipIsolates, Hex(frames.Direct));
        CollectionAssert.AreEqual(
            frames.Composited,
            frames.Direct,
            "A clip must reach the frame the same way on both paths.");

        // Named as the single byte that separates the readings, so a regression to an isolating
        // clip reports itself rather than arriving as a wall of hex.
        Assert.AreEqual(
            0,
            frames.Direct[6],
            "The blend must have multiplied against the orange beneath the clipping node: a clip "
                + "bounds a subtree, it does not give it a scope of its own.");

        // And the clip really is clipping, rather than the cyan happening to stop at x = 3: without
        // it the multiply reaches the fourth pixel too.
        group.Clip = null;
        var unclipped = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(Orange + OrangeTimesCyan + OrangeTimesCyan + OrangeTimesCyan, Hex(unclipped.Direct));
        CollectionAssert.AreEqual(unclipped.Composited, unclipped.Direct);

        // The scope a clip must not invent is exactly the one Isolate exists to ask for. Same tree,
        // same clip, one flag: the frame becomes the isolated arithmetic derived above, which is
        // the reading the old clip-isolates predicate produced for every clipped node whether or
        // not its author wanted a scope.
        group.Clip = new Rect(0f, 0f, 3f, 1f);
        group.Isolate = true;
        var isolated = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(whenClipIsolates, Hex(isolated.Direct));
        CollectionAssert.AreEqual(isolated.Composited, isolated.Direct);
    }

    [TestMethod]
    public void AClipOnlyNodeOpensNoUnitBracketAndSoStagesNoOffscreen()
    {
        // The structural half of the claim above, and the cost half of it: a clip is a saved clip,
        // never a SaveLayer. Counting what the walk asks the context for is the observation that
        // does not depend on the pixels agreeing by luck.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var group = layer.Root.Add(new Node2D { Clip = new Rect(0f, 0f, 2f, 1f) });
        group.Add(new RectDrawable(new Rect(0f, 0f, 4f, 1f), OpaqueRed));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));

        var clipOnly = new UnitCountingContext();
        scene.RenderDirect(clipOnly);

        Assert.AreEqual(1, clipOnly.ClipBracketCount, "A clipped subtree must be bracketed.");
        Assert.AreEqual(
            0,
            clipOnly.UnitBracketCount,
            "Clip state alone does not isolate, so it must not open a unit or its offscreen.");

        // Add something that genuinely isolates and both brackets appear, the clip outside the unit
        // so that it bounds what the group captures.
        group.Opacity = 0.5f;
        var clipAndOpacity = new UnitCountingContext();
        scene.RenderDirect(clipAndOpacity);

        Assert.AreEqual(1, clipAndOpacity.ClipBracketCount);
        Assert.AreEqual(1, clipAndOpacity.UnitBracketCount);

        // Drop both and the node is back on the free path: nothing is asked for at all.
        group.Opacity = 1f;
        group.Clip = null;
        var plain = new UnitCountingContext();
        scene.RenderDirect(plain);

        Assert.AreEqual(0, plain.ClipBracketCount);
        Assert.AreEqual(0, plain.UnitBracketCount);
    }

    [TestMethod]
    public void AClipBoundsWhatAnOpacityGroupCaptures_RatherThanEachPrimitiveInsideIt()
    {
        // §6.7's semantic order is clip → render node and children → composite with opacity and
        // blend, so a node that clips and isolates applies its clip OUTSIDE the group. The two
        // orderings agree wherever clip coverage is 0 or 1 — every mode in Najm's portable blend
        // subset is the identity for a transparent source — so the frame that separates them puts
        // the clip edge inside a pixel and two overlapping children across it.
        //
        // 4×1 at renderScale 1, opaque-black backdrop. A group at Opacity 0.2 with
        // Clip = (0,0,2.5,1) ⇒ device x ∈ [0,2.5), holding an opaque red over x ∈ [0,3) and an
        // opaque green over x ∈ [1,3). Pixel 2 is half covered by the clip; the antialiased clip
        // gives it coverage 0.5.
        //
        // RIGHT ORDER — clip outside the group. The children composite among themselves at full
        // alpha, so the group's content at x=2 is opaque green; the group merges at 0.2 through a
        // clip of coverage 0.5, i.e. at 0.1: (0, 0.1·255, 0) = (0, 25.5, 0) over black.
        //
        // WRONG ORDER — clip inside the group, applied to each primitive as it lands. Red arrives at
        // coverage 0.5: premultiplied (127.5, 0, 0) at α = 0.5. Green arrives at coverage 0.5 over
        // it: (0, 127.5, 0) + (1 − 0.5)·(127.5, 0, 0) = (63.75, 127.5, 0) at α = 0.75. Merging that
        // at 0.2 over black gives (12.75, 25.5, 0) — the same green, plus red that has no business
        // being there. It is the per-primitive artifact group opacity exists to prevent, arriving
        // through the clip instead.
        //
        // So the red channel of pixel 2 is the whole difference: 0 for the right order, 13 for the
        // wrong one.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D
        {
            Opacity = 0.2f,
            Clip = new Rect(0f, 0f, 2.5f, 1f),
        });
        group.Add(new RectDrawable(new Rect(0f, 0f, 3f, 1f), OpaqueRed));
        group.Add(new RectDrawable(new Rect(1f, 0f, 2f, 1f), OpaqueGreen));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        // The fully covered pixels first: 0.2·255 = 51 = 0x33, red at x=0 and green at x∈{1}, and
        // nothing at all beyond the clip.
        const string RedAtOneFifth = "330000ff";
        const string GreenAtOneFifth = "003300ff";
        Assert.AreEqual(RedAtOneFifth, Hex(frames.Direct)[..8]);
        Assert.AreEqual(GreenAtOneFifth, Hex(frames.Direct)[8..16]);
        Assert.AreEqual(Black, Hex(frames.Direct)[24..]);

        // Then the half-covered one, which is the ordering itself.
        Assert.AreEqual(
            0,
            frames.Direct[8],
            "Pixel 2 must carry no red: the clip bounds what the group captures, so the covered "
                + "child cannot leak through the clip edge one primitive at a time.");
        Assert.AreEqual(26, frames.Direct[9], "0.5 clip coverage × 0.2 group alpha × 255 = 25.5.");
        Assert.AreEqual(0, frames.Direct[10]);

        CollectionAssert.AreEqual(
            frames.Composited,
            frames.Direct,
            "A clipped group must reach the frame the same way on both paths.");
    }

    [TestMethod]
    public void ADescendantCannotPushItsWayOutOfAnAncestorsClip()
    {
        // A leaf's own PushClip composes strictly inside the bracket, so a child that clips to a
        // rectangle wider than its parent's gets the intersection and not its own wish.
        //   parent clip: device x ∈ [0,2). child PushClip: x ∈ [1,4). intersection: x ∈ [1,2).
        var expected = Black + Red + Black + Black;

        var scene = new Scene { VirtualResolution = new Vector2(4f, 1f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new Node2D { Clip = new Rect(0f, 0f, 2f, 1f) });
        group.Add(new SelfClippingRectDrawable(
            new Rect(0f, 0f, 4f, 1f),
            new Rect(1f, 0f, 3f, 1f),
            OpaqueRed));

        using var provider = new RasterSkiaSurfaceProvider();
        scene.Load(new SceneEnvironment(provider));
        var frames = RenderBothWays(scene, provider, 4, 1);

        Assert.AreEqual(expected, Hex(frames.Direct));
        CollectionAssert.AreEqual(frames.Composited, frames.Direct);
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
    public void EngineBracketsOfEveryKindMustCloseInTheOrderTheyWereOpened()
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

        // The third kind is in the same order and names itself the same way. The traverser opens a
        // clip outside a unit, so this is exactly the nesting a clipped isolating node produces.
        context.BeginClipBracket(new ClipBracket(new Rect(0f, 0f, 2f, 2f), Matrix3x2.Identity));
        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.SrcOver));
        var clipCrossed = Assert.ThrowsExactly<InvalidOperationException>(context.EndClipBracket);
        StringAssert.Contains(clipCrossed.Message, "more recently opened engine unit bracket");

        context.EndUnitBracket();
        var unitOverClip = Assert.ThrowsExactly<InvalidOperationException>(context.EndUnitBracket);
        StringAssert.Contains(unitOverClip.Message, "more recently opened engine clip bracket");
        context.EndClipBracket();

        context.EndLayerBracket();
        var unopened = Assert.ThrowsExactly<InvalidOperationException>(context.EndUnitBracket);
        StringAssert.Contains(unopened.Message, "no engine unit bracket is open");
        var noClip = Assert.ThrowsExactly<InvalidOperationException>(context.EndClipBracket);
        StringAssert.Contains(noClip.Message, "no engine clip bracket is open");
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

        // Restored means restored, and every kind is named separately, because they are owned by
        // different callers — the author, the layer walk, and the node walk's two brackets — and a
        // fix for one is not a fix for another.
        context = target.GetContext();
        context.BeginLayerBracket(new LayerBracket(OpaqueRed, 1f, BlendMode.SrcOver, null));
        context.BeginClipBracket(new ClipBracket(new Rect(0f, 0f, 2f, 2f), Matrix3x2.Identity));
        context.BeginUnitBracket(new UnitBracket(0.5f, BlendMode.SrcOver));
        context.PushClip(new Rect(0f, 0f, 1f, 1f));

        var all = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
        StringAssert.Contains(all.Message, "1 unbalanced context state push(es)");
        StringAssert.Contains(all.Message, "1 unbalanced engine layer bracket(s)");
        StringAssert.Contains(all.Message, "1 unbalanced engine unit bracket(s)");
        StringAssert.Contains(all.Message, "1 unbalanced engine clip bracket(s)");

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

    /// <summary>Fills a rectangle inside a clip it pushes and pops itself, as a leaf would.</summary>
    private sealed class SelfClippingRectDrawable : Drawable
    {
        private readonly PathBuilder path;
        private readonly Paint paint;
        private readonly Rect bounds;
        private readonly Rect ownClip;

        internal SelfClippingRectDrawable(Rect bounds, Rect ownClip, Color color)
        {
            this.bounds = bounds;
            this.ownClip = ownClip;
            paint = Paint.Fill(color, isAntialias: false);
            path = new PathBuilder(initialCapacity: 5)
                .MoveTo(bounds.X, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y)
                .LineTo(bounds.X + bounds.Width, bounds.Y + bounds.Height)
                .LineTo(bounds.X, bounds.Y + bounds.Height)
                .Close();
        }

        public override Rect GeometryBounds => bounds;

        public override void Render(IDrawContext2D context)
        {
            context.PushClip(ownClip);
            context.DrawPath(path, paint);
            context.PopClip();
        }
    }

    /// <summary>Counts the engine brackets a walk asks for, by kind, and does nothing else.</summary>
    private sealed class UnitCountingContext : DrawContext2DBase
    {
        internal int UnitBracketCount { get; private set; }

        /// <summary>Gets how many clip brackets the walk asked for.</summary>
        internal int ClipBracketCount { get; private set; }

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

        public override void BeginClipBracket(in ClipBracket bracket) => ClipBracketCount++;

        public override void EndClipBracket()
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
