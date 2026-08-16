using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// End-to-end proof that a ticked scene paints pixels: Scene → render traverser → draw context →
/// raster surface. Every expectation here is derived from the transform arithmetic in the comment
/// above it, never captured from a previous run.
/// </summary>
[TestClass]
public sealed class SceneRenderPipelineTests
{
    private const string Black = "000000ff";
    private const string Red = "ff0000ff";
    private const string Green = "00ff00ff";
    private const string Blue = "0000ffff";

    private static readonly Color OpaqueBlack = Color.Srgb(0f, 0f, 0f);
    private static readonly Color OpaqueRed = Color.Srgb(1f, 0f, 0f);
    private static readonly Color OpaqueGreen = Color.Srgb(0f, 1f, 0f);
    private static readonly Color OpaqueBlue = Color.Srgb(0f, 0f, 1f);

    [TestMethod]
    public void ScreenLayerDrawable_PaintsTheExactGoldenRectangle()
    {
        // Virtual space is 8×4 and the target is 8×4, so renderScale is 1 and virtual units are
        // pixels. The drawable's local rect (0,0)-(3,2) sits at position (2,1), so it covers
        // x ∈ [2,5) and y ∈ [1,3). The layer clears the frame to opaque black.
        const string ExpectedRgbaHex =
            Black + Black + Black + Black + Black + Black + Black + Black +
            Black + Black + Red + Red + Red + Black + Black + Black +
            Black + Black + Red + Red + Red + Black + Black + Black +
            Black + Black + Black + Black + Black + Black + Black + Black;

        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var rectangle = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 3f, 2f), OpaqueRed));
        rectangle.Position = new Vector2(2f, 1f);
        scene.Load();
        scene.Tick(Ticks.At(0));

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(target);

        Assert.AreEqual(
            ExpectedRgbaHex,
            Hex(target),
            "Rows: ........ / ..RRR... / ..RRR... / ........ over an opaque black clear.");
        Assert.AreEqual(1, rectangle.RenderCount);
    }

    [TestMethod]
    public void RenderScaleComesFromTheTargetSizeAndTheNodeMatrixIsAppliedFirst()
    {
        // The same 8×4 virtual scene on a 16×8 target: renderScale = min(16/8, 8/4) = 2.
        // Row vectors, node-then-base: local (0,0) → +(2,1) → ×2 = (4,2); local (3,2) → (5,3) →
        // (10,6). So the rectangle covers x ∈ [4,10), y ∈ [2,6).
        // The rejected base-then-node order would give (0,0)→×2→+(2,1) = (2,1) and (3,2)→(6,4)→
        // (8,5): x ∈ [2,8), y ∈ [1,5). The two disagree on every edge.
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 3f, 2f), OpaqueRed)).Position =
            new Vector2(2f, 1f);
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(16, 8));
        scene.Render(target);

        var expected = Fill(16, 8, OpaqueBlack);
        FillRegion(expected, 16, left: 4, top: 2, right: 10, bottom: 6, OpaqueRed);
        CollectionAssert.AreEqual(expected, ReadRgba(target));
    }

    [TestMethod]
    public void WorldLayerCamera_PutsPositiveWorldYTowardTheTopOfTheImage()
    {
        // Virtual space is 8×4 at renderScale 1, so the default camera at the world origin maps
        // world (x, y) to virtual (x + 4, 2 - y).
        // The red unit square sits at world y ∈ [1,2] → virtual y ∈ [0,1] → pixel row 0.
        // The green unit square sits at world y ∈ [-1,0] → virtual y ∈ [2,3] → pixel row 2.
        // Both sit at world x ∈ [0,1] → virtual x ∈ [4,5] → pixel column 4.
        const string ExpectedRgbaHex =
            Black + Black + Black + Black + Red + Black + Black + Black +
            Black + Black + Black + Black + Black + Black + Black + Black +
            Black + Black + Black + Black + Green + Black + Black + Black +
            Black + Black + Black + Black + Black + Black + Black + Black;

        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var layer = scene.Layers.Add(new WorldLayer2D { ClearColor = OpaqueBlack });
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueRed)).Position =
            new Vector2(0f, 1f);
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueGreen)).Position =
            new Vector2(0f, -1f);
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(target);

        var pixels = ReadRgba(target);
        Assert.AreEqual(ExpectedRgbaHex, Hex(pixels));
        Assert.AreEqual(0, RowOf(pixels, width: 8, height: 4, OpaqueRed), "World +Y must land on the top row.");
        Assert.AreEqual(2, RowOf(pixels, width: 8, height: 4, OpaqueGreen), "World −Y must land below it.");
        Assert.IsTrue(layer.YAxisPointsUp);

        // The same scene on a 16×8 target, renderScale 2: the camera maps to virtual first and the
        // render scale multiplies that, so red covers x ∈ [8,10), y ∈ [0,2) and green sits at
        // y ∈ [4,6). Scaling before the camera instead would leave both squares on the virtual
        // centre at (8,4)/(8,2), which is why this second size is worth rendering.
        using var scaled = provider.CreateTarget(new SurfaceSpec(16, 8));
        scene.Render(scaled);

        var expected = Fill(16, 8, OpaqueBlack);
        FillRegion(expected, 16, left: 8, top: 0, right: 10, bottom: 2, OpaqueRed);
        FillRegion(expected, 16, left: 8, top: 4, right: 10, bottom: 6, OpaqueGreen);
        CollectionAssert.AreEqual(expected, ReadRgba(scaled));
    }

    [TestMethod]
    public void ZIndexDecidesWhichNodeWinsWhereTwoNodesOverlap()
    {
        // 4×2 virtual space at renderScale 1. Red covers columns 0-2, blue covers columns 1-3;
        // they overlap on columns 1-2. Blue is inserted first but carries the higher ZIndex, so
        // paint order is red then blue and blue owns the overlap.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var blue = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 3f, 2f), OpaqueBlue) { ZIndex = 5 });
        blue.Position = new Vector2(1f, 0f);
        var red = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 3f, 2f), OpaqueRed) { ZIndex = 1 });
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(
            Red + Blue + Blue + Blue +
            Red + Blue + Blue + Blue,
            Hex(target),
            "The higher ZIndex paints last and therefore wins the overlap.");

        // Reversing the keys reverses the winner without touching insertion order.
        blue.ZIndex = 0;
        red.ZIndex = 1;
        scene.Render(target);

        Assert.AreEqual(
            Red + Red + Red + Blue +
            Red + Red + Red + Blue,
            Hex(target));
    }

    [TestMethod]
    public void VisibleFalseHidesTheSubtreeInPixelsWhileEnabledFalseDoesNot()
    {
        // 4×2 at renderScale 1: the parent paints columns 0-1, its child paints over column 1 —
        // pre-order puts the parent underneath — and an unrelated sibling paints column 3.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var parent = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueRed));
        var child = parent.Add(new RectDrawable(new Rect(0f, 0f, 1f, 2f), OpaqueGreen));
        child.Position = new Vector2(1f, 0f);
        var sibling = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 1f, 2f), OpaqueBlue));
        sibling.Position = new Vector2(3f, 0f);
        scene.Load();
        scene.Tick(Ticks.At(0));

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));

        parent.Visible = false;
        scene.Render(target);

        Assert.AreEqual(
            Black + Black + Black + Blue +
            Black + Black + Black + Blue,
            Hex(target),
            "An invisible node must take its whole subtree out of the frame.");

        scene.Tick(Ticks.At(1));

        Assert.AreEqual(2, parent.UpdateCount, "An invisible node must keep updating.");
        Assert.AreEqual(2, child.UpdateCount, "An invisible node's subtree must keep updating.");

        parent.Visible = true;
        parent.Enabled = false;
        scene.Tick(Ticks.At(2));
        scene.Render(target);

        Assert.AreEqual(
            Red + Green + Black + Blue +
            Red + Green + Black + Blue,
            Hex(target),
            "A disabled node and its subtree must still render, with the child painting over its parent.");
        Assert.AreEqual(2, parent.UpdateCount, "A disabled node must stop updating.");
        Assert.AreEqual(2, child.UpdateCount, "A disabled node's subtree must stop updating.");
        Assert.AreEqual(3, sibling.UpdateCount, "Only the disabled subtree is affected.");
    }

    [TestMethod]
    public void ALayerThatCannotContributeIsNotCleared_NotWalked_AndDoesNotRunItsHooks()
    {
        // The bottom layer would clear the frame red and paint it green; the layer above it clears
        // blue and paints nothing. While the bottom layer cannot contribute, the frame is blue.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var bottom = scene.Layers.Add(new HookLayer { ClearColor = OpaqueRed });
        bottom.Root.Add(new RectDrawable(new Rect(0f, 0f, 4f, 2f), OpaqueGreen));
        scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlue });
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));

        bottom.Visible = false;
        scene.Render(target);

        Assert.AreEqual(Repeat(Blue, 8), Hex(target), "A hidden layer contributes neither its clear nor its tree.");
        Assert.AreEqual(0, bottom.BeforeCount);
        Assert.AreEqual(0, bottom.AfterCount);

        bottom.Visible = true;
        bottom.Opacity = 0f;
        scene.Render(target);

        Assert.AreEqual(Repeat(Blue, 8), Hex(target), "A fully transparent layer contributes nothing either.");
        Assert.AreEqual(0, bottom.BeforeCount);
        Assert.AreEqual(0, bottom.AfterCount);

        bottom.Opacity = 1f;
        scene.Render(target);

        Assert.AreEqual(Repeat(Green, 8), Hex(target), "Restoring the layer restores its clear and its tree.");
        Assert.AreEqual(1, bottom.BeforeCount);
        Assert.AreEqual(1, bottom.AfterCount);
    }

    [TestMethod]
    public void LayerHooksPaintUnderAndOverTheTreeWalk()
    {
        // OnBeforeRender floods 4×2 red, the tree paints columns 0-1 green, OnAfterRender paints
        // column 3 blue. Only a before → tree → after ordering produces this frame.
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new HookLayer
        {
            ClearColor = OpaqueBlack,
            Before = new RectDrawable(new Rect(0f, 0f, 4f, 2f), OpaqueRed),
            After = new RectDrawable(new Rect(3f, 0f, 1f, 2f), OpaqueBlue),
        });
        layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);

        Assert.AreEqual(
            Green + Green + Red + Blue +
            Green + Green + Red + Blue,
            Hex(target));
    }

    [TestMethod]
    public void TickOnceRenderTwice_IsByteIdenticalAndChangesNothingObservable()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var screen = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var overlay = screen.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 1f), OpaqueBlue));
        overlay.Position = new Vector2(5f, 2f);
        var world = scene.Layers.Add(new WorldLayer2D());
        var moving = world.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));
        moving.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueRed)).Position = new Vector2(-2f, 0f);
        scene.Load();
        scene.Tick(Ticks.At(0));

        var worldMatrix = moving.WorldMatrix;
        var position = moving.Position;
        var layerCount = scene.Layers.Count;
        var updateCount = moving.UpdateCount;

        using var provider = new RasterSkiaSurfaceProvider();
        using var first = provider.CreateTarget(new SurfaceSpec(8, 4));
        using var second = provider.CreateTarget(new SurfaceSpec(8, 4));
        scene.Render(first);
        scene.Render(second);
        var firstPixels = ReadRgba(first);

        CollectionAssert.AreEqual(firstPixels, ReadRgba(second), "Two renders of one tick must be byte identical.");

        scene.Render(first);

        CollectionAssert.AreEqual(firstPixels, ReadRgba(first), "Re-rendering into the same target must be stable.");
        Assert.AreEqual(worldMatrix, moving.WorldMatrix);
        Assert.AreEqual(position, moving.Position);
        Assert.AreEqual(layerCount, scene.Layers.Count);
        Assert.AreEqual(updateCount, moving.UpdateCount, "Rendering must not run any update.");
        Assert.AreEqual(SceneState.Started, scene.State);

        scene.Tick(Ticks.At(1));

        Assert.AreEqual(updateCount + 1, moving.UpdateCount, "Rendering must not disturb the tick sequence.");
    }

    [TestMethod]
    public void RenderBeforeTheFirstUpdate_PaintsTheSameFrameTheFirstTickWould()
    {
        var scene = new Scene { VirtualResolution = new Vector2(4f, 2f) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var node = layer.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueGreen));
        scene.Load();

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 2));
        scene.Render(target);
        var beforeAnyTick = ReadRgba(target);

        Assert.AreEqual(0, node.UpdateCount, "This frame must be legal before the node has ever updated.");
        Assert.AreEqual(1, node.RenderCount);
        Assert.AreEqual(
            Green + Green + Black + Black +
            Green + Green + Black + Black,
            Hex(beforeAnyTick));

        scene.Tick(Ticks.At(0));
        scene.Render(target);

        CollectionAssert.AreEqual(beforeAnyTick, ReadRgba(target));
        Assert.AreEqual(1, node.UpdateCount);
    }

    [TestMethod]
    public void WarmSceneRenderLoop_AllocatesNoManagedBytes()
    {
        var scene = new Scene { VirtualResolution = new Vector2(8f, 4f) };
        var screen = scene.Layers.Add(new ScreenLayer { ClearColor = OpaqueBlack });
        var parent = screen.Root.Add(new RectDrawable(new Rect(0f, 0f, 2f, 2f), OpaqueRed));
        parent.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueGreen) { ZIndex = 2 });
        parent.Add(new RectDrawable(new Rect(1f, 1f, 1f, 1f), OpaqueBlue) { ZIndex = -2 });
        var world = scene.Layers.Add(new WorldLayer2D());
        world.Root.Add(new RectDrawable(new Rect(0f, 0f, 1f, 1f), OpaqueGreen)).Position = new Vector2(1f, 1f);
        scene.Load();
        scene.Tick(Ticks.At(0));

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(8, 4));
        for (var warmup = 0; warmup < 64; warmup++)
        {
            scene.Render(target);
        }

        const int measuredRenders = 2_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var render = 0; render < measuredRenders; render++)
        {
            scene.Render(target);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, allocated, $"The warm end-to-end render loop allocated {allocated} managed bytes.");
    }

    private static byte[] Fill(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        FillRegion(pixels, width, 0, 0, width, height, color);
        return pixels;
    }

    private static void FillRegion(byte[] pixels, int width, int left, int top, int right, int bottom, Color color)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = ToByte(color.R);
                pixels[offset + 1] = ToByte(color.G);
                pixels[offset + 2] = ToByte(color.B);
                pixels[offset + 3] = ToByte(color.A);
            }
        }
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);

    private static int RowOf(byte[] pixels, int width, int height, Color color)
    {
        var wanted = new[] { ToByte(color.R), ToByte(color.G), ToByte(color.B), ToByte(color.A) };
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                if (pixels[offset] == wanted[0] &&
                    pixels[offset + 1] == wanted[1] &&
                    pixels[offset + 2] == wanted[2] &&
                    pixels[offset + 3] == wanted[3])
                {
                    return y;
                }
            }
        }

        return -1;
    }

    private static string Repeat(string pixel, int count) => string.Concat(Enumerable.Repeat(pixel, count));

    private static string Hex(IRenderTarget target) => Hex(ReadRgba(target));

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

        public override Rect GeometryBounds => bounds;

        internal int RenderCount { get; private set; }

        internal int UpdateCount { get; private set; }

        public override void Render(IDrawContext2D context)
        {
            RenderCount++;
            context.DrawPath(path, paint);
        }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }

    private sealed class HookLayer : ScreenLayer
    {
        internal RectDrawable? Before { get; init; }

        internal RectDrawable? After { get; init; }

        internal int BeforeCount { get; private set; }

        internal int AfterCount { get; private set; }

        protected override void OnBeforeRender(IDrawContext2D context)
        {
            BeforeCount++;
            Before?.Render(context);
        }

        protected override void OnAfterRender(IDrawContext2D context)
        {
            AfterCount++;
            After?.Render(context);
        }
    }

    private static class Ticks
    {
        internal static TickContext At(long frame) =>
            new(new TimeInfo(frame + 1d, 1d, frame, isFixedStep: true));
    }
}
