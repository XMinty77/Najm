using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// Proves <see cref="GpuSkiaSurfaceProvider.WrapBackbuffer"/> adopts a framebuffer the provider did
/// not create, renders the same frame into it as into a provider-created target, and gives it back
/// untouched.
/// </summary>
/// <remarks>
/// <para>
/// A desktop host's framebuffer is the window's; there is no window here, so the test builds an
/// ordinary GL framebuffer with a texture attachment and wraps that. Every property the seam
/// promises — the bottom-left origin flip, the adopted-not-owned lifetime, the transparent
/// letterbox bars a host is expected to paint over — is a property of wrapping <em>a</em>
/// framebuffer, not of that framebuffer being a window's.
/// </para>
/// <para>
/// The orientation assertion is the one worth naming. Every other Najm surface is top-left origin
/// and this one is not, so a frame drawn through it would come out vertically mirrored if the flip
/// were missed — and a mirrored frame of a symmetric test image passes every check that reads
/// return codes. The scene here is deliberately asymmetric top to bottom.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class WrappedBackbufferTests
{
    private const int Width = 64;
    private const int Height = 48;

    [TestMethod]
    public void WrappedFramebuffer_RendersTheFrameAProviderCreatedTargetDoes()
    {
        using var fixture = GpuFixture.Require();
        using var framebuffer = GlFramebuffer.Create(Width, Height);

        byte[] wrapped;
        using (var target = fixture.Provider.WrapBackbuffer(
            new PixelSize(Width, Height),
            sampleCount: 0,
            stencilBits: 0,
            ColorSpace.Srgb,
            framebuffer.Id))
        {
            wrapped = RenderAndRead(fixture.Provider, target);
        }

        byte[] created;
        using (var target = fixture.Provider.CreateTarget(new SurfaceSpec(Width, Height)))
        {
            created = RenderAndRead(fixture.Provider, target);
        }

        var difference = GpuPixels.Compare(wrapped, created, tolerance: 4);
        Assert.IsTrue(
            difference.MeanAbsolute < 1.0,
            $"A wrapped framebuffer must carry the same frame as a created target: {difference.Describe()}.");

        // Named separately from the aggregate: the halves of TwoBandScene differ by far more than
        // any coverage tolerance, so this is the assertion a vertical flip fails.
        var top = GpuPixels.At(wrapped, Width, Width / 2, 4);
        var bottom = GpuPixels.At(wrapped, Width, Width / 2, Height - 5);
        Assert.IsGreaterThan(200, top.R, "The scene's top band is red; a flipped frame reads blue here.");
        Assert.IsGreaterThan(200, bottom.B, "The scene's bottom band is blue.");
    }

    [TestMethod]
    public void WrappedFramebuffer_ReportsTheFramebufferItAdoptedAndClampsZeroSamples()
    {
        using var fixture = GpuFixture.Require();
        using var framebuffer = GlFramebuffer.Create(Width, Height);

        using var target = fixture.Provider.WrapBackbuffer(
            new PixelSize(Width, Height),
            sampleCount: 0,
            stencilBits: 8,
            ColorSpace.Srgb,
            framebuffer.Id);

        Assert.AreEqual(new PixelSize(Width, Height), target.Size);
        Assert.AreEqual(
            1,
            target.SurfaceSpec.SampleCount,
            "GL answers 0 for a single-sampled window and SurfaceSpec rejects 0, so the wrap clamps.");
        Assert.AreEqual(ColorSpace.Srgb, target.SurfaceSpec.ColorSpace);
        Assert.AreEqual(
            RenderCaps.SkiaSurface | RenderCaps.GpuBacked,
            target.GetContext().Caps);
    }

    [TestMethod]
    public void DisposingTheWrap_LeavesTheFramebufferAndReWrapsCleanly()
    {
        using var fixture = GpuFixture.Require();
        using var framebuffer = GlFramebuffer.Create(Width, Height);

        var target = fixture.Provider.WrapBackbuffer(
            new PixelSize(Width, Height),
            sampleCount: 1,
            stencilBits: 0,
            ColorSpace.Srgb,
            framebuffer.Id);
        target.Dispose();

        Assert.AreNotEqual(
            0,
            TestGl.glIsFramebuffer(framebuffer.Id),
            "The wrap adopts the framebuffer; disposing it must not delete what the host owns.");

        // The host's resize path is exactly this: drop the old wrap, take a new one.
        using var rewrapped = fixture.Provider.WrapBackbuffer(
            new PixelSize(Width, Height),
            sampleCount: 1,
            stencilBits: 0,
            ColorSpace.Srgb,
            framebuffer.Id);
        Assert.AreEqual(new PixelSize(Width, Height), rewrapped.Size);
    }

    [TestMethod]
    public void AnOutputWiderThanTheSceneCentresTheFrameAndLeavesTheBarsTransparent()
    {
        // §5.1's letterbox, seen from the surface a host presents. The scene is 1:1 and the
        // framebuffer is 2:1, so the fitted frame is 48 wide with 24-pixel bars either side — and
        // those bars come out *transparent*, because §5.3's final merge is a replace-blit of an
        // accumulation surface that was cleared to transparent. Painting them HostOptions.BarColor
        // is the host's job, and it necessarily happens after this render rather than before it.
        using var fixture = GpuFixture.Require();
        using var framebuffer = GlFramebuffer.Create(96, 48);
        using var target = fixture.Provider.WrapBackbuffer(
            new PixelSize(96, 48),
            sampleCount: 1,
            stencilBits: 0,
            ColorSpace.Srgb,
            framebuffer.Id);

        var scene = new TwoBandScene(new Vector2(48f, 48f));
        scene.Load(new SceneEnvironment(fixture.Provider, caps: fixture.Provider.Caps));
        try
        {
            scene.Render(target);
            fixture.Provider.Flush();

            var pixels = GpuPixels.Read(target);
            Assert.AreEqual(
                (byte)0,
                GpuPixels.At(pixels, 96, 4, 24).A,
                "The left bar is outside the content rect and no path in the engine paints it.");
            Assert.AreEqual(
                (byte)0,
                GpuPixels.At(pixels, 96, 91, 24).A,
                "The right bar likewise.");
            Assert.IsGreaterThan(
                200,
                GpuPixels.At(pixels, 96, 48, 4).R,
                "The content rect starts at x = 24 and carries the scene's top band.");
            Assert.IsGreaterThan(
                200,
                GpuPixels.At(pixels, 96, 48, 43).B,
                "…and its bottom band, the right way up.");
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    [TestMethod]
    public void WrapBackbuffer_RefusesShapesItCannotDescribe()
    {
        using var fixture = GpuFixture.Require();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => fixture.Provider.WrapBackbuffer(default, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => fixture.Provider.WrapBackbuffer(new PixelSize(8, 8), -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => fixture.Provider.WrapBackbuffer(new PixelSize(8, 8), 1, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => fixture.Provider.WrapBackbuffer(new PixelSize(8, 8), 1, 0, (ColorSpace)99));
    }

    private static byte[] RenderAndRead(GpuSkiaSurfaceProvider provider, IRenderTarget target)
    {
        var scene = new TwoBandScene(new Vector2(Width, Height));
        scene.Load(new SceneEnvironment(provider, caps: provider.Caps));
        try
        {
            scene.Render(target);
            provider.Flush();
            return GpuPixels.Read(target);
        }
        finally
        {
            scene.Stop();
            scene.Unload();
        }
    }

    /// <summary>Red over the top half, blue over the bottom — asymmetric, so a flip is visible.</summary>
    private sealed class TwoBandScene : Scene
    {
        internal TwoBandScene(Vector2 virtualResolution)
        {
            VirtualResolution = virtualResolution;
            var layer = Layers.Add(new ScreenLayer());
            layer.Root.Add(new BandNode(virtualResolution));
        }
    }

    private sealed class BandNode(Vector2 size) : Drawable
    {
        private readonly Paint top = Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false);
        private readonly Paint bottom = Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false);

        public override Rect GeometryBounds => new(0f, 0f, size.X, size.Y);

        public override void Render(IDrawContext2D context)
        {
            context.DrawRect(new Rect(0f, 0f, size.X, size.Y / 2f), top);
            context.DrawRect(new Rect(0f, size.Y / 2f, size.X, size.Y / 2f), bottom);
        }
    }

    /// <summary>A GL framebuffer with a colour texture, standing in for a window's.</summary>
    private sealed class GlFramebuffer : IDisposable
    {
        private uint[] framebuffers = [];
        private uint[] textures = [];

        internal uint Id => framebuffers[0];

        internal static GlFramebuffer Create(int width, int height)
        {
            var instance = new GlFramebuffer
            {
                framebuffers = new uint[1],
                textures = new uint[1],
            };

            TestGl.glGenTextures(1, instance.textures);
            TestGl.glBindTexture(TestGl.Texture2D, instance.textures[0]);
            TestGl.glTexImage2D(
                TestGl.Texture2D, 0, (int)TestGl.Rgba8, width, height, 0,
                TestGl.Rgba, TestGl.UnsignedByte, IntPtr.Zero);
            TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureMinFilter, TestGl.Nearest);
            TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureMagFilter, TestGl.Nearest);

            TestGl.glGenFramebuffers(1, instance.framebuffers);
            TestGl.glBindFramebuffer(TestGl.Framebuffer, instance.framebuffers[0]);
            TestGl.glFramebufferTexture2D(
                TestGl.Framebuffer, TestGl.ColorAttachment0, TestGl.Texture2D, instance.textures[0], 0);

            var status = TestGl.glCheckFramebufferStatus(TestGl.Framebuffer);
            TestGl.glBindFramebuffer(TestGl.Framebuffer, 0);
            if (status != TestGl.FramebufferComplete)
            {
                instance.Dispose();
                Assert.Inconclusive($"Could not build a complete GL framebuffer to wrap (status 0x{status:X4}).");
            }

            return instance;
        }

        public void Dispose()
        {
            if (framebuffers.Length > 0)
            {
                TestGl.glDeleteFramebuffers(1, framebuffers);
                framebuffers = [];
            }

            if (textures.Length > 0)
            {
                TestGl.glDeleteTextures(1, textures);
                textures = [];
            }
        }
    }
}
