using System.Numerics;
using Najm.Utils;
using Najm.Core.Text;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class RenderTraverserTests
{
    [TestMethod]
    public void VirtualResolutionDefaultsToFullHdAndRejectsNonPositiveSizes()
    {
        Assert.AreEqual(new Vector2(1920f, 1080f), new Scene().VirtualResolution);
        Assert.AreEqual(
            new Vector2(640f, 480f),
            new Scene { VirtualResolution = new Vector2(640f, 480f) }.VirtualResolution);

        foreach (var invalid in new[]
                 {
                     new Vector2(0f, 1080f),
                     new Vector2(1920f, 0f),
                     new Vector2(-1920f, 1080f),
                     new Vector2(float.NaN, 1080f),
                     new Vector2(1920f, float.PositiveInfinity),
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => _ = new Scene { VirtualResolution = invalid });
        }
    }

    [TestMethod]
    public void ScreenLayerBaseIsTheRenderScaleAndWorldLayerBaseCarriesTheCameraFlip()
    {
        var resolution = new Vector2(8f, 4f);
        var screenBase = RenderTraverser.ComputeLayerBase(new ScreenLayer(), resolution, 2f);

        Assert.AreEqual(Matrix3x2.CreateScale(2f), screenBase);
        Assert.AreEqual(new Vector2(6f, 2f), Vector2.Transform(new Vector2(3f, 1f), screenBase));

        var world = new WorldLayer2D();
        var worldBase = RenderTraverser.ComputeLayerBase(world, resolution, 2f);

        // World origin sits on the virtual centre (4, 2), which is device (8, 4) at twice scale;
        // one world unit of +Y lands one virtual unit toward the top, i.e. device (8, 2).
        AssertPoint(new Vector2(8f, 4f), Vector2.Transform(Vector2.Zero, worldBase));
        AssertPoint(new Vector2(8f, 2f), Vector2.Transform(Vector2.UnitY, worldBase));
        AssertPoint(new Vector2(10f, 4f), Vector2.Transform(Vector2.UnitX, worldBase));
    }

    [TestMethod]
    public void AViewportdWorldLayerFramesItsViewportAndSitsAtTheViewportsFrameOrigin()
    {
        // An 8×4 frame at renderScale 2. The world layer occupies the virtual rect (4,0)-(8,4), so
        // its camera frames a 4×4 extent whose centre is viewport-local virtual (2,2); the viewport
        // origin carries that to frame virtual (6,2), which is device (12,4) at twice scale. One
        // world unit of +Y is one virtual unit toward the top, device (12,2); one of +X is device
        // (14,4). Framing the scene's 8×4 instead would put the world origin on frame virtual (4,2),
        // i.e. device (8,4) — a frame-centred world that the viewport then merely crops.
        var resolution = new Vector2(8f, 4f);
        var viewport = new Rect(4f, 0f, 4f, 4f);
        var world = new WorldLayer2D { Viewport = viewport };
        var worldBase = RenderTraverser.ComputeLayerBase(world, resolution, 2f);

        AssertPoint(new Vector2(12f, 4f), Vector2.Transform(Vector2.Zero, worldBase));
        AssertPoint(new Vector2(12f, 2f), Vector2.Transform(Vector2.UnitY, worldBase));
        AssertPoint(new Vector2(14f, 4f), Vector2.Transform(Vector2.UnitX, worldBase));

        // A ScreenLayer has no camera, so its viewport reframes nothing: its nodes stay in absolute
        // virtual coordinates and the backend places the target's device origin.
        var screenBase = RenderTraverser.ComputeLayerBase(new ScreenLayer { Viewport = viewport }, resolution, 2f);

        Assert.AreEqual(Matrix3x2.CreateScale(2f), screenBase);
    }

    [TestMethod]
    public void EngineTransformAppliesTheNodeWorldMatrixBeforeTheLayerBase()
    {
        var scene = new Scene { VirtualResolution = new Vector2(100f, 100f) };
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(10f, 0f);
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 3f);
        scene.RenderDirect(context);

        // world × base: local (0,0) translates to (10,0) and then scales to (30,0).
        // The rejected base × world order would scale first and land on (10,0).
        var engine = log.Single("node");
        AssertPoint(new Vector2(30f, 0f), Vector2.Transform(Vector2.Zero, engine));
        AssertPoint(new Vector2(33f, 3f), Vector2.Transform(Vector2.One, engine));
        Assert.AreEqual(node.WorldMatrix * Matrix3x2.CreateScale(3f), engine);
    }

    [TestMethod]
    public void NestedNodesComposeParentThenLayerBase()
    {
        var scene = new Scene { VirtualResolution = new Vector2(100f, 100f) };
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var parent = layer.Root.Add(new LoggingDrawable("parent", log));
        parent.Position = new Vector2(4f, 0f);
        parent.Scale = new Vector2(2f, 2f);
        var child = parent.Add(new LoggingDrawable("child", log));
        child.Position = new Vector2(1f, 0f);
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(context);

        // Child local (0,0) → parent local (1,0) → layer (4,0)+(2,0) = (6,0) → device (12,0).
        AssertPoint(new Vector2(12f, 0f), Vector2.Transform(Vector2.Zero, log.Single("child")));
        AssertPoint(new Vector2(8f, 0f), Vector2.Transform(Vector2.Zero, log.Single("parent")));
    }

    [TestMethod]
    public void TraversalIsDepthFirstPreOrderInPaintOrder()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var front = layer.Root.Add(new LoggingDrawable("front", log) { ZIndex = 5 });
        front.Add(new LoggingDrawable("front.child", log));
        var back = layer.Root.Add(new LoggingDrawable("back", log) { ZIndex = -1 });
        back.Add(new LoggingDrawable("back.child", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("back,back.child,front,front.child", log.Names);
    }

    [TestMethod]
    public void LayerHooksBracketTheWalkAndSeeTheLayerBaseTransform()
    {
        var scene = new Scene { VirtualResolution = new Vector2(64f, 32f) };
        var log = new RenderLog();
        var layer = scene.Layers.Add(new HookLayer("layer", log));
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(5f, 5f);
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext(renderScale: 2f));

        Assert.AreEqual("layer.before,node,layer.after", log.Names);
        Assert.AreEqual(Matrix3x2.CreateScale(2f), log.Single("layer.before"));
        Assert.AreEqual(
            Matrix3x2.CreateScale(2f),
            log.Single("layer.after"),
            "OnAfterRender must see layer space again, not the last node's transform.");
    }

    [TestMethod]
    public void LayersRenderInAddOrderAndSkipWholeWhenTheyCannotContribute()
    {
        var scene = new Scene();
        var log = new RenderLog();
        var hidden = scene.Layers.Add(new HookLayer("hidden", log));
        hidden.Root.Add(new LoggingDrawable("hidden.node", log));
        hidden.Visible = false;
        var transparent = scene.Layers.Add(new HookLayer("transparent", log));
        transparent.Root.Add(new LoggingDrawable("transparent.node", log));
        transparent.Opacity = 0f;
        var shown = scene.Layers.Add(new HookLayer("shown", log));
        shown.Root.Add(new LoggingDrawable("shown.node", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("shown.before,shown.node,shown.after", log.Names);
        Assert.IsFalse(RenderTraverser.ParticipatesInRender(hidden));
        Assert.IsFalse(RenderTraverser.ParticipatesInRender(transparent));
        Assert.IsTrue(RenderTraverser.ParticipatesInRender(shown));

        log.Clear();
        hidden.Visible = true;
        transparent.Opacity = 1f;
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(
            "hidden.before,hidden.node,hidden.after," +
            "transparent.before,transparent.node,transparent.after," +
            "shown.before,shown.node,shown.after",
            log.Names);
    }

    [TestMethod]
    public void VisibleFalseSkipsTheSubtreeAndEnabledFalseOnlySkipsUpdate()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var invisible = layer.Root.Add(new LoggingDrawable("invisible", log) { Visible = false });
        var invisibleChild = invisible.Add(new LoggingDrawable("invisible.child", log));
        var disabled = layer.Root.Add(new LoggingDrawable("disabled", log) { Enabled = false });
        var disabledChild = disabled.Add(new LoggingDrawable("disabled.child", log));
        scene.Load(TestEnvironment.Stub());

        scene.Tick(Ticks.At(0));
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(
            "disabled,disabled.child",
            log.Names,
            "Visible gates rendering for the whole subtree; Enabled must not.");
        Assert.AreEqual(1, invisible.UpdateCount, "An invisible node must still update.");
        Assert.AreEqual(1, invisibleChild.UpdateCount, "An invisible subtree must still update.");
        Assert.AreEqual(0, disabled.UpdateCount, "A disabled node must not update.");
        Assert.AreEqual(0, disabledChild.UpdateCount, "A disabled subtree must not update.");
    }

    [TestMethod]
    public void RenderDirectFoldsTheContextRenderScaleIntoEveryEngineTransform()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        layer.Root.Add(new LoggingDrawable("node", log)).Position = new Vector2(3f, 7f);
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext(renderScale: 1.5f));

        AssertPoint(new Vector2(4.5f, 10.5f), Vector2.Transform(Vector2.Zero, log.Single("node")));
    }

    [TestMethod]
    public void RenderIsLegalBeforeTheFirstTickAndLeavesTheSceneReadyToStart()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        scene.Load(TestEnvironment.Stub());

        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual("node", log.Names);
        Assert.AreEqual(0, node.UpdateCount, "A node may render before it has ever updated.");
        Assert.AreEqual(SceneState.Loaded, scene.State);

        scene.Tick(Ticks.At(0));

        Assert.AreEqual(1, node.UpdateCount);
        Assert.AreEqual(SceneState.Started, scene.State);
    }

    [TestMethod]
    public void RenderRequiresALoadedSceneAndRejectsReentrancy()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var context = new RecordingContext();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.RenderDirect(context));

        scene.Load(TestEnvironment.Stub());
        var reentrant = layer.Root.Add(new ReentrantDrawable(scene, context));
        scene.RenderDirect(context);

        Assert.IsInstanceOfType<InvalidOperationException>(reentrant.Failure);
        Assert.ThrowsExactly<ArgumentNullException>(() => scene.RenderDirect(null!));

        scene.Stop();
        scene.Unload();

        Assert.ThrowsExactly<InvalidOperationException>(() => scene.RenderDirect(context));
    }

    [TestMethod]
    public void RenderDirectDoesNotMutateObservableSceneState()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var log = new RenderLog();
        var node = layer.Root.Add(new LoggingDrawable("node", log));
        node.Position = new Vector2(2f, 3f);
        scene.Load(TestEnvironment.Stub());
        scene.Tick(Ticks.At(0));

        var world = node.WorldMatrix;
        var position = node.Position;
        var updateCount = node.UpdateCount;

        scene.RenderDirect(new RecordingContext());
        scene.RenderDirect(new RecordingContext());

        Assert.AreEqual(world, node.WorldMatrix);
        Assert.AreEqual(position, node.Position);
        Assert.AreEqual(updateCount, node.UpdateCount);
        Assert.AreEqual(SceneState.Started, scene.State);
        Assert.AreEqual(1, scene.Layers.Count);
        Assert.AreSame(layer, node.Layer);

        scene.Tick(Ticks.At(1));

        Assert.AreEqual(2, node.UpdateCount, "Rendering must not disturb the tick sequence.");
    }

    [TestMethod]
    public void EveryParticipatingLayerIsWalkedInsideABracketCarryingItsPresentation()
    {
        // The direct path binds no per-layer target, so a layer's clear, opacity, blend, and
        // viewport can only reach the backend on the bracket. A layer that cannot contribute must
        // not even open one: its clear is content it does not get to contribute, exactly as the
        // compositor never binds or clears it.
        var red = Color.Srgb(1f, 0f, 0f);
        var blue = Color.Srgb(0f, 0f, 1f);
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        scene.Layers.Add(new ScreenLayer { ClearColor = red });
        scene.Layers.Add(new ScreenLayer { Opacity = 0.5f, Blend = BlendMode.Multiply });
        scene.Layers.Add(new ScreenLayer { ClearColor = blue, Visible = false });
        scene.Layers.Add(new ScreenLayer { ClearColor = blue, Opacity = 0f });
        scene.Layers.Add(new ScreenLayer { ClearColor = blue, Viewport = new Rect(2f, 1f, 3f, 2f) });
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(context);

        Assert.HasCount(3, context.Brackets, "The invisible and zero-opacity layers open none.");
        Assert.AreEqual(new LayerBracket(red, 1f, BlendMode.SrcOver, null), context.Brackets[0]);
        Assert.AreEqual(
            new LayerBracket(Color.Transparent, 0.5f, BlendMode.Multiply, null),
            context.Brackets[1]);

        // The viewport rides the bracket in device pixels: (2,1,3,2) virtual at renderScale 2 is
        // (4,2) with a 6×4 extent, which is the same integer rectangle the compositor would have
        // staged that layer through.
        Assert.AreEqual(
            new LayerBracket(blue, 1f, BlendMode.SrcOver, new Rect(4f, 2f, 6f, 4f)),
            context.Brackets[2]);
        Assert.AreEqual(0, context.BracketDepth, "Every bracket the walk opened must have closed.");
        Assert.AreEqual(3, context.BracketCount);
    }

    [TestMethod]
    public void TheBracketEnclosesBothLayerHooksAndEveryEngineTransform()
    {
        // The structural point of the whole seam: the engine transform is set per node *inside* an
        // open bracket. Author state cannot do this — SetEngineTransform rejects an outstanding
        // author push — which is why the bracket is engine-owned and counted separately.
        var scene = new Scene();
        var log = new RenderLog();
        var layer = scene.Layers.Add(new HookLayer("layer", log));
        layer.Root.Add(new LoggingDrawable("node", log));
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        // Bracket, then the layer base, then OnBeforeRender, then one engine transform for the
        // layer's own root node and one for the drawable beneath it, then the layer base again for
        // OnAfterRender, then the close.
        Assert.AreEqual(
            "open1,engine,layer.before,engine,engine,node,engine,layer.after,close1",
            context.Events);
    }

    [TestMethod]
    public void ABracketsDeviceViewportRoundsExactlyAsTheCompositorPlacesItsSurface()
    {
        // Origin rounds to the pixel grid, extent rounds outward — SkiaCompositor.ResolvePlacement's
        // rule, so a fractional viewport covers the same pixels whichever path draws it.
        // At renderScale 2: x = round(2.4·2) = round(4.8) = 5, y = round(1.6·2) = round(3.2) = 3,
        // width = ceil(3.3·2) = ceil(6.6) = 7, height = ceil(2.2·2) = ceil(4.4) = 5.
        var scene = new Scene { VirtualResolution = new Vector2(16f, 8f) };
        var framed = scene.Layers.Add(new ScreenLayer { Viewport = new Rect(2.4f, 1.6f, 3.3f, 2.2f) });
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(context);

        Assert.AreEqual(new Rect(5f, 3f, 7f, 5f), context.Brackets[0].Viewport);

        // A midpoint origin pins the tie-break as MathF.Round's, which is to even: 1.25·2 = 2.5 → 2,
        // and 3.75·2 = 7.5 → 8. Rounding half away from zero would give 3 and 8 instead.
        framed.Viewport = new Rect(1.25f, 3.75f, 1f, 1f);
        var second = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(second);

        Assert.AreEqual(new Rect(2f, 8f, 2f, 2f), second.Brackets[0].Viewport);

        // A full-frame layer has no viewport at all, and must not be handed a frame-sized rectangle
        // that would clip geometry the compositor lets overhang its target.
        framed.Viewport = null;
        var third = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(third);

        Assert.IsNull(third.Brackets[0].Viewport);
    }

    [TestMethod]
    public void ABracketRejectsAnOpacityOutsideTheUnitInterval()
    {
        foreach (var invalid in new[] { -0.001f, 1.001f, float.NaN, float.PositiveInfinity })
        {
            var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => _ = new LayerBracket(Color.Black, invalid, BlendMode.SrcOver, null));
            Assert.AreEqual("opacity", rejected.ParamName);
        }

        var bracket = new LayerBracket(Color.Black, 0.5f, BlendMode.Screen, new Rect(1f, 2f, 3f, 4f));

        Assert.AreEqual(Color.Black, bracket.Clear);
        Assert.AreEqual(0.5f, bracket.Opacity);
        Assert.AreEqual(BlendMode.Screen, bracket.Blend);
        Assert.AreEqual(new Rect(1f, 2f, 3f, 4f), bracket.Viewport);
        Assert.AreEqual(default, new LayerBracket(Color.Transparent, 0f, BlendMode.SrcOver, null));
    }

    [TestMethod]
    public void OnlyANodeWhoseCompositionRequiresIsolationOpensAUnitBracket()
    {
        // §6.7's predicate, restricted to M1's three properties. The default node is the case that
        // has to stay free: no bracket, no offscreen group, nothing to pay for.
        var scene = new Scene();
        var layer = scene.Layers.Add(new ScreenLayer());
        var plain = layer.Root.Add(new Node2D());
        var faded = layer.Root.Add(new Node2D { Opacity = 0.5f });
        var blended = layer.Root.Add(new Node2D { Blend = BlendMode.Multiply });
        var forced = layer.Root.Add(new Node2D { Isolate = true });
        var clipped = layer.Root.Add(new Node2D { Clip = new Rect(0f, 0f, 1f, 1f) });
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        // Three isolating nodes among five, plus the layer's own root, which is a plain Node2D. The
        // clipped one is not among them: §6.7's predicate is blend, mask, effect, backdrop, opacity
        // below one, and Isolate — a clip bounds without isolating and gets its own bracket.
        Assert.HasCount(3, context.Units);
        Assert.HasCount(1, context.Clips);
        Assert.AreEqual(new UnitBracket(0.5f, BlendMode.SrcOver), context.Units[0]);
        Assert.AreEqual(new UnitBracket(1f, BlendMode.Multiply), context.Units[1]);
        Assert.AreEqual(new UnitBracket(1f, BlendMode.SrcOver), context.Units[2]);
        Assert.AreEqual(0, context.UnitDepth, "Every unit the walk opened must have closed.");

        // Restoring each property to its default takes the node back off the isolating path, so the
        // predicate is read per frame rather than latched when the property was first set.
        faded.Opacity = 1f;
        blended.Blend = BlendMode.SrcOver;
        forced.Isolate = false;
        clipped.Clip = null;
        var quiet = new RecordingContext();
        scene.RenderDirect(quiet);

        Assert.AreEqual(0, quiet.UnitCount, "Five default nodes must open nothing at all.");
        Assert.AreEqual(0, quiet.ClipCount);
        Assert.IsFalse(plain.Isolate);
    }

    [TestMethod]
    public void AClipAloneOpensAClipBracketAndNoUnitAtAll()
    {
        // §6.7's table: clip state alone does not isolate. So a node whose only composition state is
        // a clip asks for the bracket that bounds and never for the one that stages an offscreen.
        //
        // The clip is stated in the node's local coordinates, and the bracket opens before that
        // node's engine transform is installed — opening one sheds whatever transform was there.
        // So the rectangle cannot travel alone: the mapping it is read under goes with it, and it is
        // the very transform the traverser installs on the next line.
        var scene = new Scene { VirtualResolution = new Vector2(100f, 50f) };
        var layer = scene.Layers.Add(new ScreenLayer());
        var clipped = layer.Root.Add(new Node2D
        {
            Position = new Vector2(7f, 3f),
            Clip = new Rect(0f, 0f, 20f, 10f),
        });
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(context);

        Assert.AreEqual(0, context.UnitCount, "A clip must not isolate: no unit, no offscreen.");
        Assert.HasCount(1, context.Clips);
        Assert.AreEqual(new Rect(0f, 0f, 20f, 10f), context.Clips[0].Clip);

        // translate(7, 3) under a screen layer at render scale 2: scale 2 with the translation
        // doubled into device pixels.
        var expected = Matrix3x2.CreateTranslation(7f, 3f) * Matrix3x2.CreateScale(2f);
        Assert.AreEqual(expected, context.Clips[0].ClipToDevice);
        Assert.AreEqual(new Vector2(14f, 6f), context.Clips[0].ClipToDevice.Translation);
        Assert.AreEqual(0, context.ClipDepth, "Every clip the walk opened must have closed.");

        // Clearing it takes the node off the clipped path entirely: the state is read per frame,
        // never latched.
        clipped.Clip = null;
        var quiet = new RecordingContext(renderScale: 2f);
        scene.RenderDirect(quiet);

        Assert.AreEqual(0, quiet.ClipCount);
        Assert.AreEqual(0, quiet.UnitCount);
    }

    [TestMethod]
    public void AClipThatAlsoIsolatesOpensTheClipOutsideTheUnit()
    {
        // §6.7's semantic order is clip → render node and children → composite with opacity and
        // blend, so the clip has to be in force when the unit's group opens: it bounds what that
        // group captures rather than being applied inside it. Two brackets, clip outermost, and the
        // clip closes last.
        var scene = new Scene();
        var log = new RenderLog();
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new LoggingDrawable("group", log));
        group.Clip = new Rect(0f, 0f, 4f, 4f);
        group.Opacity = 0.5f;
        group.Add(new LoggingDrawable("child", log));
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        Assert.AreEqual(
            "open1,engine,engine,clip+1,unit+1,engine,group,engine,child,unit-1,clip-1,engine,close1",
            context.Events);
        Assert.AreEqual(new UnitBracket(0.5f, BlendMode.SrcOver), context.Units[0]);
        Assert.AreEqual(new Rect(0f, 0f, 4f, 4f), context.Clips[0].Clip);
    }

    [TestMethod]
    public void AClippedNodesBracketStillSpansItsWholeSubtree()
    {
        // The point of putting the clip on a bracket rather than in each leaf's Render: it opens
        // before the node paints and closes after the last descendant, so it bounds the subtree.
        var scene = new Scene();
        var log = new RenderLog();
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new LoggingDrawable("group", log));
        group.Clip = new Rect(0f, 0f, 4f, 4f);
        group.Add(new LoggingDrawable("child", log));
        layer.Root.Add(new LoggingDrawable("sibling", log));
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        Assert.AreEqual(
            "open1,engine,engine,clip+1,engine,group,engine,child,clip-1,engine,sibling,engine,close1",
            context.Events);
    }

    [TestMethod]
    public void AClipBracketRejectsANonFiniteClipMappingAndCarriesTheRectItWasGiven()
    {
        var broken = new Matrix3x2(1f, 0f, 0f, float.NaN, 0f, 0f);

        var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _ = new ClipBracket(new Rect(0f, 0f, 1f, 1f), broken));
        Assert.AreEqual("clipToDevice", rejected.ParamName);

        var bracket = new ClipBracket(new Rect(1f, 2f, 3f, 4f), Matrix3x2.CreateScale(2f));

        Assert.AreEqual(new Rect(1f, 2f, 3f, 4f), bracket.Clip);
        Assert.AreEqual(Matrix3x2.CreateScale(2f), bracket.ClipToDevice);

        // An empty rectangle is a legitimate thing to say — it hides the subtree — and is exactly
        // what default(ClipBracket) says under the zero matrix.
        Assert.AreEqual(default, new ClipBracket(default, default));
    }

    [TestMethod]
    public void AUnitBracketEnclosesTheNodesOwnPaintAndItsWholeSubtree()
    {
        // The unit is the node plus its descendants, which is what makes the opacity a group
        // operation. It opens before the node's own engine transform — opening sheds whatever
        // transform the previous sibling installed — and closes after the last descendant.
        var scene = new Scene();
        var log = new RenderLog();
        var layer = scene.Layers.Add(new ScreenLayer());
        var group = layer.Root.Add(new LoggingDrawable("group", log));
        group.Opacity = 0.5f;
        group.Add(new LoggingDrawable("child", log));
        layer.Root.Add(new LoggingDrawable("sibling", log));
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        Assert.AreEqual(
            "open1,engine,engine,unit+1,engine,group,engine,child,unit-1,engine,sibling,engine,close1",
            context.Events);
    }

    [TestMethod]
    public void NestedIsolatingNodesNestTheirUnitsAndAnInvisibleOneOpensNothing()
    {
        var scene = new Scene();
        var log = new RenderLog();
        var layer = scene.Layers.Add(new ScreenLayer());
        var outer = layer.Root.Add(new LoggingDrawable("outer", log));
        outer.Opacity = 0.5f;
        var inner = outer.Add(new LoggingDrawable("inner", log));
        inner.Blend = BlendMode.Screen;
        scene.Load(TestEnvironment.Stub());

        var context = new RecordingContext();
        scene.RenderDirect(context);

        Assert.AreEqual(
            "open1,engine,engine,unit+1,engine,outer,unit+2,engine,inner,unit-2,unit-1,engine,close1",
            context.Events);

        // Visibility is decided before isolation, so a hidden unit costs no bracket either: an
        // invisible subtree that still opened its group would be a pure fill-rate leak.
        outer.Visible = false;
        var hidden = new RecordingContext();
        scene.RenderDirect(hidden);

        Assert.AreEqual(0, hidden.UnitCount);
    }

    [TestMethod]
    public void AUnitBracketRejectsAnOpacityOutsideTheUnitInterval()
    {
        foreach (var invalid in new[] { -0.001f, 1.001f, float.NaN, float.NegativeInfinity })
        {
            var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => _ = new UnitBracket(invalid, BlendMode.SrcOver));
            Assert.AreEqual("opacity", rejected.ParamName);
        }

        var bracket = new UnitBracket(0.25f, BlendMode.Screen);

        Assert.AreEqual(0.25f, bracket.Opacity);
        Assert.AreEqual(BlendMode.Screen, bracket.Blend);
        Assert.AreEqual(default, new UnitBracket(0f, BlendMode.SrcOver));
    }

    [TestMethod]
    public void WarmRenderTraversalAllocatesNoManagedMemory()
    {
        var scene = new Scene();
        var screen = scene.Layers.Add(new ScreenLayer());
        var world = scene.Layers.Add(new WorldLayer2D());
        var parent = screen.Root.Add(new SilentDrawable());
        parent.Add(new SilentDrawable { ZIndex = 3 });
        parent.Add(new SilentDrawable { ZIndex = -3 });
        world.Root.Add(new SilentDrawable()).Position = new Vector2(1f, 2f);
        scene.Load(TestEnvironment.Stub());
        scene.Tick(Ticks.At(0));

        var context = new SilentContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        var reading = AllocationProbe.AssertNoneAllocated(
            10_000,
            () => scene.RenderDirect(context),
            "The warm render traversal");

        // Four drawables and two layer brackets, every render. The probe owns the render count, so
        // both expectations are derived from it rather than fixed.
        Assert.AreEqual((64 + reading.Invocations) * 4, context.DrawCount);
        Assert.AreEqual((64 + reading.Invocations) * 2, context.BracketCount);
        Assert.AreEqual(0, context.BracketDepth);
    }

    [TestMethod]
    public void AWarmWalkOverIsolatingNodesAllocatesNoManagedMemory()
    {
        // The bracket is a struct passed by reference and the predicate is three field reads, so
        // isolation costs the managed heap nothing per frame however many nodes take it.
        var scene = new Scene();
        var screen = scene.Layers.Add(new ScreenLayer());
        var faded = screen.Root.Add(new SilentDrawable());
        faded.Opacity = 0.5f;
        var blended = faded.Add(new SilentDrawable());
        blended.Blend = BlendMode.Multiply;
        var forced = screen.Root.Add(new SilentDrawable());
        forced.Isolate = true;
        scene.Load(TestEnvironment.Stub());
        scene.Tick(Ticks.At(0));

        var context = new SilentContext();
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.RenderDirect(context);
        }

        var reading = AllocationProbe.AssertNoneAllocated(
            10_000,
            () => scene.RenderDirect(context),
            "The warm render traversal over isolating nodes");

        // Three isolating nodes, every render. The probe owns the render count.
        Assert.AreEqual((64 + reading.Invocations) * 3, context.UnitCount);
        Assert.AreEqual(0, context.UnitDepth);
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-4f, $"Expected {expected} but mapped to {actual}.");
        Assert.AreEqual(expected.Y, actual.Y, 1e-4f, $"Expected {expected} but mapped to {actual}.");
    }

    private sealed class RenderLog
    {
        private readonly List<(string Name, Matrix3x2 Engine)> entries = [];

        internal string Names => string.Join(',', entries.Select(entry => entry.Name));

        internal void Add(string name, in Matrix3x2 engine) => entries.Add((name, engine));

        internal void Clear() => entries.Clear();

        internal Matrix3x2 Single(string name) =>
            entries.Single(entry => entry.Name == name).Engine;
    }

    private sealed class LoggingDrawable(string name, RenderLog log) : Drawable
    {
        internal int UpdateCount { get; private set; }

        public override void Render(IDrawContext2D context)
        {
            var recording = (RecordingContext)context;
            log.Add(name, recording.Engine);
            recording.Note(name);
        }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }

    private sealed class ReentrantDrawable(Scene scene, IDrawContext2D context) : Drawable
    {
        internal Exception? Failure { get; private set; }

        public override void Render(IDrawContext2D drawContext)
        {
            try
            {
                scene.RenderDirect(context);
            }
            catch (Exception exception)
            {
                Failure = exception;
            }
        }
    }

    private sealed class SilentDrawable : Drawable
    {
        public override void Render(IDrawContext2D context) => context.DrawPath(EmptyPath, default);

        private static readonly PathBuilder EmptyPath = new();
    }

    private sealed class HookLayer(string name, RenderLog log) : ScreenLayer
    {
        protected override void OnBeforeRender(IDrawContext2D context)
        {
            var recording = (RecordingContext)context;
            log.Add($"{name}.before", recording.Engine);
            recording.Note($"{name}.before");
        }

        protected override void OnAfterRender(IDrawContext2D context)
        {
            var recording = (RecordingContext)context;
            log.Add($"{name}.after", recording.Engine);
            recording.Note($"{name}.after");
        }
    }

    private class SilentContext : DrawContext2DBase
    {
        internal SilentContext(float renderScale = 1f) => RenderScale = renderScale;

        public override SurfaceSpec SurfaceSpec { get; } = new(64, 64);

        public override RenderCaps Caps => RenderCaps.None;

        public override float RenderScale { get; }

        public override float Scale => 1f;

        internal int DrawCount { get; private set; }

        /// <summary>Gets how many engine layer brackets are open right now.</summary>
        internal int BracketDepth { get; private set; }

        /// <summary>Gets how many engine layer brackets have been opened in total.</summary>
        internal int BracketCount { get; private set; }

        /// <summary>Gets how many engine unit brackets are open right now.</summary>
        internal int UnitDepth { get; private set; }

        /// <summary>Gets how many engine unit brackets have been opened in total.</summary>
        internal int UnitCount { get; private set; }

        /// <summary>Gets how many engine clip brackets are open right now.</summary>
        internal int ClipDepth { get; private set; }

        /// <summary>Gets how many engine clip brackets have been opened in total.</summary>
        internal int ClipCount { get; private set; }

        public override void Clear(Color color)
        {
        }

        public override void DrawPath(PathBuilder path, in Paint paint) => DrawCount++;

        public override void DrawText(ITextLayout layout, Color? colorOverride = null) => DrawCount++;

        public override void DrawImage(IImage image, in Matrix3x2 imageToLocal, ImageSampling sampling = ImageSampling.Linear)
        {
        }

        public override void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
        }

        public override void BeginLayerBracket(in LayerBracket bracket)
        {
            BracketDepth++;
            BracketCount++;
        }

        public override void EndLayerBracket()
        {
            if (BracketDepth == 0)
            {
                throw new InvalidOperationException("No engine layer bracket is open.");
            }

            BracketDepth--;
        }

        public override void BeginUnitBracket(in UnitBracket bracket)
        {
            UnitDepth++;
            UnitCount++;
        }

        public override void EndUnitBracket()
        {
            if (UnitDepth == 0)
            {
                throw new InvalidOperationException("No engine unit bracket is open.");
            }

            UnitDepth--;
        }

        public override void BeginClipBracket(in ClipBracket bracket)
        {
            ClipDepth++;
            ClipCount++;
        }

        public override void EndClipBracket()
        {
            if (ClipDepth == 0)
            {
                throw new InvalidOperationException("No engine clip bracket is open.");
            }

            ClipDepth--;
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

    private sealed class RecordingContext(float renderScale = 1f) : SilentContext(renderScale)
    {
        private readonly List<LayerBracket> brackets = [];
        private readonly List<UnitBracket> units = [];
        private readonly List<ClipBracket> clips = [];
        private readonly List<string> events = [];

        internal Matrix3x2 Engine { get; private set; }

        /// <summary>Gets every bracket opened, in the order the traverser opened them.</summary>
        internal IReadOnlyList<LayerBracket> Brackets => brackets;

        /// <summary>Gets every unit bracket opened, in the order the traverser opened them.</summary>
        internal IReadOnlyList<UnitBracket> Units => units;

        /// <summary>Gets every clip bracket opened, in the order the traverser opened them.</summary>
        internal IReadOnlyList<ClipBracket> Clips => clips;

        /// <summary>Gets the interleaving of brackets, engine transforms, hooks, and node paints.</summary>
        internal string Events => string.Join(',', events);

        internal void Note(string what) => events.Add(what);

        public override void SetEngineTransform(in Matrix3x2 engineToDevice)
        {
            Engine = engineToDevice;
            events.Add("engine");
        }

        public override void BeginLayerBracket(in LayerBracket bracket)
        {
            base.BeginLayerBracket(bracket);
            brackets.Add(bracket);
            events.Add($"open{BracketDepth}");
        }

        public override void EndLayerBracket()
        {
            events.Add($"close{BracketDepth}");
            base.EndLayerBracket();
        }

        public override void BeginUnitBracket(in UnitBracket bracket)
        {
            base.BeginUnitBracket(bracket);
            units.Add(bracket);
            events.Add($"unit+{UnitDepth}");
        }

        public override void EndUnitBracket()
        {
            events.Add($"unit-{UnitDepth}");
            base.EndUnitBracket();
        }

        public override void BeginClipBracket(in ClipBracket bracket)
        {
            base.BeginClipBracket(bracket);
            clips.Add(bracket);
            events.Add($"clip+{ClipDepth}");
        }

        public override void EndClipBracket()
        {
            events.Add($"clip-{ClipDepth}");
            base.EndClipBracket();
        }
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
