using System.Numerics;
using Najm.Core;
using Najm.Skia;
using Najm.Utils;

namespace Najm.Host.Desktop.Tests;

/// <summary>
/// The classic host bug, tested the only way that catches it: click the pixel, name the node.
/// </summary>
/// <remarks>
/// <para>
/// A host can get the picture right and the clicks wrong, and every check that reads a return code
/// passes while it does. So this test never states a device coordinate of its own. It renders the
/// frame at an output aspect that differs from the scene's, <strong>finds a pixel by the colour of
/// the node that painted it</strong>, maps that pixel back through <see cref="Letterbox"/> exactly
/// as <c>DesktopHost</c> maps a pointer, and asserts the press arrives at that node — through the
/// real <see cref="InputRouter"/>, from a real <see cref="InputBuffer"/>, on a real
/// <see cref="Scene"/>.
/// </para>
/// <para>
/// There is no window here and the test does not want one. The letterbox mapping, the router walk
/// and the frame placement are all platform-independent; what a window adds is GLFW, X11 and a GL
/// context, which is what the Xvfb script covers and a unit test cannot.
/// </para>
/// </remarks>
[TestClass]
public sealed class LetterboxedRoutingTests
{
    private static readonly Vector2 Widescreen = new(1920f, 1080f);

    /// <summary>A 2:1 output for a 16:9 scene — 80 device pixels of bar on each side.</summary>
    private static readonly PixelSize MismatchedOutput = new(1440, 720);

    [TestMethod]
    public void TheRenderedFrameOccupiesExactlyTheRectangleTheLetterboxNames()
    {
        using var surfaces = new RasterSkiaSurfaceProvider();
        using var target = surfaces.CreateTarget(new SurfaceSpec(MismatchedOutput.Width, MismatchedOutput.Height));
        var scene = new PatchworkScene();

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        try
        {
            scene.Render(target);
            var frame = Frame.Read(target);
            var painted = frame.PaintedBounds();
            var box = Letterbox.Resolve(scene.VirtualResolution, MismatchedOutput);

            Assert.AreEqual(
                box.ContentRect,
                painted,
                "The host's letterbox and the compositor's placement must name the same rectangle.");
            Assert.IsTrue(box.HasBars, "This output aspect is chosen precisely because it bars.");
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    [TestMethod]
    public void ClickingThePixelANodePaintedReachesThatNode()
    {
        using var surfaces = new RasterSkiaSurfaceProvider();
        using var target = surfaces.CreateTarget(new SurfaceSpec(MismatchedOutput.Width, MismatchedOutput.Height));
        var scene = new PatchworkScene();

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        try
        {
            scene.Render(target);
            var frame = Frame.Read(target);
            var box = Letterbox.Resolve(scene.VirtualResolution, MismatchedOutput);
            var clock = new FrameClock(ClockPolicy.Live(0.25d));
            var input = new InputBuffer();

            foreach (var patch in scene.Patches)
            {
                // The device pixel is found in the image, not computed from the layout: it is
                // wherever this patch's colour actually landed after fitting and centring.
                var devicePixel = frame.CentroidOf(patch.Fill);
                var virtualPoint = box.ToVirtual(devicePixel);

                input.BeginFrame();
                input.MovePointer(0, virtualPoint);
                input.PressPointer(0, virtualPoint, PointerButton.Left);
                scene.Tick(new TickContext(clock.Advance(1d / 60d), input.Block));

                Assert.AreEqual(
                    patch,
                    scene.LastPressed,
                    $"The pixel at {devicePixel} shows {patch.Name}; the press has to arrive there.");
            }

            Assert.AreEqual(3, scene.PressCount);
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    [TestMethod]
    public void ClickingABarReachesNoNode()
    {
        // The bar is not the scene, and §9.1 delivers the coordinate anyway — negative, unclamped —
        // so this proves the router declines it on bounds rather than the host swallowing it.
        using var surfaces = new RasterSkiaSurfaceProvider();
        var scene = new PatchworkScene();

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        try
        {
            var box = Letterbox.Resolve(scene.VirtualResolution, MismatchedOutput);
            var barPoint = box.ToVirtual(new Vector2(20f, 360f));
            Assert.IsLessThan(0f, barPoint.X, "A point on the left bar maps to a negative virtual X.");

            var input = new InputBuffer();
            input.BeginFrame();
            input.PressPointer(0, barPoint, PointerButton.Left);
            scene.Tick(new TickContext(
                new FrameClock(ClockPolicy.Live(0.25d)).Advance(1d / 60d),
                input.Block));

            Assert.AreEqual(0, scene.PressCount);
            Assert.IsNull(scene.LastPressed);
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    [TestMethod]
    public void TheSameClickReachesADifferentNodeWhenTheOutputAspectChanges()
    {
        // The point of the whole exercise, stated as one assertion: the same physical pixel is a
        // different part of the scene on a differently shaped window, and a host that resolved the
        // mapping once and cached it would fail here rather than at the demo.
        using var surfaces = new RasterSkiaSurfaceProvider();
        var scene = new PatchworkScene();

        scene.Load(new SceneEnvironment(surfaces, caps: surfaces.Caps));
        try
        {
            var clock = new FrameClock(ClockPolicy.Live(0.25d));
            var input = new InputBuffer();
            // 600 device pixels across: 780 virtual on the pillarboxed 2:1 output, 1500 on the
            // letterboxed 16:15 one. Different bands, same pixel.
            var devicePixel = new Vector2(600f, 360f);

            var wide = Letterbox.Resolve(scene.VirtualResolution, MismatchedOutput);
            input.BeginFrame();
            input.PressPointer(0, wide.ToVirtual(devicePixel), PointerButton.Left);
            scene.Tick(new TickContext(clock.Advance(1d / 60d), input.Block));
            var onWide = scene.LastPressed;

            var narrow = Letterbox.Resolve(scene.VirtualResolution, new PixelSize(768, 720));
            input.BeginFrame();
            input.PressPointer(0, narrow.ToVirtual(devicePixel), PointerButton.Left);
            scene.Tick(new TickContext(clock.Advance(1d / 60d), input.Block));
            var onNarrow = scene.LastPressed;

            Assert.AreEqual("middle", onWide?.Name);
            Assert.AreEqual("right", onNarrow?.Name);
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    /// <summary>Three vertical bands of flat colour, each of which reports its own presses.</summary>
    private sealed class PatchworkScene : Scene
    {
        internal PatchworkScene()
        {
            VirtualResolution = Widescreen;
            var layer = Layers.Add(new ScreenLayer());
            Patches =
            [
                Add(layer, "left", Color.Srgb(1f, 0f, 0f), 0f),
                Add(layer, "middle", Color.Srgb(0f, 1f, 0f), 640f),
                Add(layer, "right", Color.Srgb(0f, 0f, 1f), 1280f),
            ];
        }

        internal Patch[] Patches { get; }

        internal Patch? LastPressed { get; private set; }

        internal int PressCount { get; private set; }

        private Patch Add(ScreenLayer layer, string name, Color fill, float x)
        {
            var patch = new Patch(name, fill, new Vector2(640f, Widescreen.Y), OnPressed)
            {
                Position = new Vector2(x, 0f),
            };
            layer.Root.Add(patch);
            return patch;
        }

        private void OnPressed(Patch patch)
        {
            LastPressed = patch;
            PressCount++;
        }
    }

    private sealed class Patch(string name, Color fill, Vector2 size, Action<Patch> onPressed)
        : Drawable, IInteractive
    {
        private readonly Paint paint = Paint.Fill(fill, isAntialias: false);

        internal string Name => name;

        internal Color Fill => fill;

        public override Rect GeometryBounds => new(0f, 0f, size.X, size.Y);

        public override void Render(IDrawContext2D context) =>
            context.DrawRect(new Rect(0f, 0f, size.X, size.Y), paint);

        public bool OnPointerDown(in PointerArgs args)
        {
            onPressed(this);
            return true;
        }

        public override string ToString() => name;
    }

    /// <summary>A rendered frame, read once and asked questions about afterwards.</summary>
    private readonly struct Frame(byte[] pixels, int width, int height)
    {
        internal static Frame Read(IRenderTarget target)
        {
            using var snapshot = target.Snapshot();
            var pixels = new byte[checked(target.Size.Width * target.Size.Height * 4)];
            snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
            return new Frame(pixels, target.Size.Width, target.Size.Height);
        }

        /// <summary>Returns the smallest rectangle containing every pixel anything painted.</summary>
        internal Rect PaintedBounds()
        {
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(((y * width) + x) * 4) + 3] == 0)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right < 0
                ? default
                : new Rect(left, top, right - left + 1, bottom - top + 1);
        }

        /// <summary>Returns the average position of every pixel carrying one colour.</summary>
        /// <remarks>
        /// The centroid rather than the first match, so the point is comfortably inside the band
        /// rather than on the seam between two of them where a half-pixel of rounding decides the
        /// answer.
        /// </remarks>
        internal Vector2 CentroidOf(Color color)
        {
            var wanted = ToBytes(color);
            double sumX = 0, sumY = 0;
            var count = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = ((y * width) + x) * 4;
                    if (pixels[index] != wanted.R ||
                        pixels[index + 1] != wanted.G ||
                        pixels[index + 2] != wanted.B ||
                        pixels[index + 3] != 255)
                    {
                        continue;
                    }

                    sumX += x;
                    sumY += y;
                    count++;
                }
            }

            Assert.IsGreaterThan(0, count, "No pixel in the frame carries this colour.");

            // The half-pixel puts the sample at the centre of the pixel rather than its corner,
            // which is where the colour it was read from actually is.
            return new Vector2((float)(sumX / count) + 0.5f, (float)(sumY / count) + 0.5f);
        }

        private static (byte R, byte G, byte B) ToBytes(Color color) => (
            (byte)Math.Round(Math.Clamp(color.R, 0f, 1f) * 255f),
            (byte)Math.Round(Math.Clamp(color.G, 0f, 1f) * 255f),
            (byte)Math.Round(Math.Clamp(color.B, 0f, 1f) * 255f));
    }
}
