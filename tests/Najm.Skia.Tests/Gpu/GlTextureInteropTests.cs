using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// End-to-end proof of the interop seam: an author's GLSL ES pipeline renders into a texture the
/// author owns, Najm wraps it as an <see cref="IImage"/>, and it composites through
/// <see cref="IDrawContext2D.DrawImage"/> like any other image.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GlTextureInteropTests
{
    private const int TextureExtent = 64;
    private const int TargetWidth = 128;
    private const int TargetHeight = 96;

    /// <summary>Halves the 64-pixel texture and parks it at (20,12), so it covers (20,12)–(52,44).</summary>
    private static Matrix3x2 Placement =>
        Matrix3x2.CreateScale(0.5f) * Matrix3x2.CreateTranslation(20f, 12f);

    private static Color Backdrop => Color.Srgb(0f, 0f, 0f, 0f);

    [TestMethod]
    public void AuthorRenderedTexture_LandsWhereTheTransformSaysItShould()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        Assert.AreEqual(pipeline.Size, image.Size);

        var pixels = DrawWrapped(fixture, image);

        // The shader's own coordinates put red then green along the bottom and blue then white along
        // the top. Declared top-left, memory row zero is the top row, so GL's bottom half shows up
        // at the top of the drawn image.
        AssertPixel(pixels, 24, 16, 255, 0, 0, "the shader's bottom-left quadrant");
        AssertPixel(pixels, 48, 16, 0, 255, 0, "the shader's bottom-right quadrant");
        AssertPixel(pixels, 24, 40, 0, 0, 255, "the shader's top-left quadrant");
        AssertPixel(pixels, 48, 40, 255, 255, 255, "the shader's top-right quadrant");
        AssertPixel(pixels, 36, 28, 0, 0, 0, "the shader's centre disc");

        // And nowhere else. A wrap drawn at the wrong transform would paint here.
        Assert.AreEqual((byte)0, GpuPixels.At(pixels, TargetWidth, 4, 4).A, "Outside the placement, nothing.");
        Assert.AreEqual((byte)0, GpuPixels.At(pixels, TargetWidth, 60, 60).A, "Below the placement, nothing.");
        Assert.AreEqual((byte)0, GpuPixels.At(pixels, TargetWidth, 18, 28).A, "Left of the placement, nothing.");
    }

    [TestMethod]
    public void Origin_IsTheVerticalFlipItSoundsLike_AndIsNotGuessed()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var topLeft = DrawWrapped(
            fixture,
            fixture.Provider.WrapGlTexture(
                pipeline.TextureId,
                pipeline.Size,
                new GlTextureOptions { Origin = GlTextureOrigin.TopLeft }));
        var bottomLeft = DrawWrapped(
            fixture,
            fixture.Provider.WrapGlTexture(
                pipeline.TextureId,
                pipeline.Size,
                new GlTextureOptions { Origin = GlTextureOrigin.BottomLeft }));

        // GL renders bottom-up: for render-to-texture content, BottomLeft is the truthful answer and
        // is what shows the shader's own top half at the top.
        AssertPixel(bottomLeft, 24, 16, 0, 0, 255, "the shader's top-left quadrant, declared bottom-left");
        AssertPixel(bottomLeft, 48, 16, 255, 255, 255, "the shader's top-right quadrant, declared bottom-left");
        AssertPixel(bottomLeft, 24, 40, 255, 0, 0, "the shader's bottom-left quadrant, declared bottom-left");
        AssertPixel(bottomLeft, 48, 40, 0, 255, 0, "the shader's bottom-right quadrant, declared bottom-left");

        // Row for row, one is the other upside down inside the placement.
        for (var y = 12; y < 44; y++)
        {
            var mirrored = 55 - y;
            for (var x = 20; x < 52; x += 4)
            {
                Assert.AreEqual(
                    GpuPixels.At(topLeft, TargetWidth, x, y),
                    GpuPixels.At(bottomLeft, TargetWidth, x, mirrored),
                    $"Row {y} of the top-left wrap must be row {mirrored} of the bottom-left one.");
            }
        }
    }

    [TestMethod]
    public void Wrap_IsCachedPerTextureAndRebuiltOnlyWhenTheTextureIsReallocated()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var first = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        var second = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);

        Assert.AreSame(first, second, "A stable texture must not be re-wrapped.");

        // A resize reallocates storage under the same GL name. The wrap is rebuilt in place, so a
        // reference the author kept stays correct rather than going stale.
        pipeline.Reallocate(TextureExtent / 2, TextureExtent / 2);
        pipeline.Render();
        fixture.Provider.ResetGlState();
        var resized = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);

        Assert.AreSame(first, resized);
        Assert.AreEqual(new PixelSize(TextureExtent / 2, TextureExtent / 2), resized.Size);
        Assert.AreEqual(new PixelSize(TextureExtent / 2, TextureExtent / 2), first.Size);

        // A different interpretation of the same storage is also a rebuild.
        var flipped = fixture.Provider.WrapGlTexture(
            pipeline.TextureId,
            pipeline.Size,
            new GlTextureOptions { Origin = GlTextureOrigin.BottomLeft });
        Assert.AreSame(first, flipped);
        Assert.AreEqual(GlTextureOrigin.BottomLeft, flipped.Options.Origin);
    }

    [TestMethod]
    public void WarmRedrawOfAStableTexture_AllocatesNoManagedBytes()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(TargetWidth, TargetHeight));
        var context = target.GetContext();
        var placement = Placement;
        var draws = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            256,
            () =>
            {
                // Exactly what an author writes inside a render method: ask for the wrap, draw it.
                var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
                context.DrawImage(image, placement, ImageSampling.Nearest);
                draws++;
            },
            "wrapping and drawing a stable GL texture");

        Assert.AreEqual(reading.Invocations, draws);
    }

    [TestMethod]
    public void FromTextureBorrows_SoTheTextureSurvivesDisposingItsImageAndRewraps()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        var before = DrawWrapped(fixture, image);
        image.Dispose();
        fixture.Provider.Flush();

        Assert.IsTrue(pipeline.TextureExists, "Disposing the image must not delete the author's texture.");
        Assert.IsFalse(
            fixture.Provider.ReleaseGlTexture(pipeline.TextureId),
            "A disposed wrap must have left the provider's cache.");

        var rewrapped = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        Assert.AreNotSame(image, rewrapped);
        var after = DrawWrapped(fixture, rewrapped);

        CollectionAssert.AreEqual(before, after, "The same texture id must re-wrap and sample identically.");
        Assert.ThrowsExactly<ObjectDisposedException>(() => image.CopyPixels(new byte[16], PixelFormat.Rgba8888));
    }

    [TestMethod]
    public void ReleaseHandshake_FiresWhenSkiaLetsGo_WhichIsNotAlwaysAtDisposal()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        // With recorded work still referencing the texture, disposal is not enough: Skia holds the
        // texture until the work that samples it has been flushed and submitted. This is the case
        // the handshake exists for, and the one an author hits inside a frame loop.
        var released = 0;
        uint releasedId = 0;
        var pending = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        pending.TextureReleased = id =>
        {
            released++;
            releasedId = id;
        };

        using (var target = fixture.Provider.CreateTarget(new SurfaceSpec(TargetWidth, TargetHeight)))
        {
            var context = target.GetContext();
            context.Clear(Backdrop);
            context.DrawImage(pending, Placement, ImageSampling.Nearest);
            Assert.AreEqual(0, released, "A drawn, live wrap has not been released.");

            pending.Dispose();
            Assert.AreEqual(
                0,
                released,
                "Disposal does not release a texture the GPU has unflushed work against.");

            fixture.Provider.Flush();
            Assert.AreEqual(1, released, "Flushing is what makes deleting the texture safe.");
            Assert.AreEqual(pipeline.TextureId, releasedId);
        }

        // With nothing outstanding, the same handshake completes at disposal itself. Both orders
        // reach the same place, which is why the rule an author follows is dispose-then-flush rather
        // than "count the flushes".
        var settled = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        var settledReleases = 0;
        settled.TextureReleased = _ => settledReleases++;
        DrawWrapped(fixture, settled);
        Assert.AreEqual(0, settledReleases, "Drawing and flushing does not release a live wrap.");
        settled.Dispose();
        Assert.AreEqual(1, settledReleases, "With the work already submitted, disposal is the release.");
        fixture.Provider.Flush();
        Assert.AreEqual(1, settledReleases, "The handshake fires once, not once per flush.");

        // And only now is this correct.
        pipeline.DeleteTexture();
        Assert.IsFalse(pipeline.TextureExists);
    }

    [TestMethod]
    public void RepeatedWrapAndDisposeCycles_DoNotAccumulateTextures()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var nameBefore = ProbeNextTextureName();
        for (var cycle = 0; cycle < 200; cycle++)
        {
            var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
            image.Dispose();
        }

        fixture.Provider.Flush();
        fixture.Provider.ResetGlState();
        var nameAfter = ProbeNextTextureName();

        Assert.IsTrue(pipeline.TextureExists, "200 wrap/dispose cycles must leave the author's texture alone.");
        Assert.IsFalse(fixture.Provider.ReleaseGlTexture(pipeline.TextureId), "The wrap cache must be empty.");

        // GL hands out the lowest free name. If the cycles had leaked a texture apiece the free list
        // would have moved by hundreds; a small drift is Skia's own scratch resources, not ours.
        Assert.IsLessThanOrEqualTo(
            nameBefore + 8,
            nameAfter,
            $"The GL texture-name pool moved from {nameBefore} to {nameAfter} across 200 cycles.");
    }

    [TestMethod]
    public void WrappedImage_ReadsBackItsOwnPixels()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();

        var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);
        var pixels = new byte[TextureExtent * TextureExtent * 4];
        image.CopyPixels(pixels, PixelFormat.Rgba8888);

        // Memory row zero holds the shader's bottom row: red on the left, green on the right.
        AssertPixel(pixels, 8, 2, 255, 0, 0, "texture memory row 2, left", TextureExtent);
        AssertPixel(pixels, 56, 2, 0, 255, 0, "texture memory row 2, right", TextureExtent);
        Assert.ThrowsExactly<ArgumentException>(() => image.CopyPixels(new byte[16], PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentException>(() => image.CopyPixels(pixels, (PixelFormat)99));
    }

    [TestMethod]
    public void WrapRequestsAreValidatedBeforeAnythingNativeHappens()
    {
        using var fixture = GpuFixture.Require();
        var provider = fixture.Provider;
        var size = new PixelSize(8, 8);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => provider.WrapGlTexture(0, size));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => provider.WrapGlTexture(1, new PixelSize(provider.MaxTextureSize + 1, 8)));
        Assert.ThrowsExactly<ArgumentException>(
            () => provider.WrapGlTexture(1, size, new GlTextureOptions { Origin = (GlTextureOrigin)7 }));
        Assert.ThrowsExactly<ArgumentException>(
            () => provider.WrapGlTexture(1, size, new GlTextureOptions { ColorSpace = (ColorSpace)7 }));
        Assert.ThrowsExactly<ArgumentException>(
            () => provider.WrapGlTexture(1, size, new GlTextureOptions { SizedFormat = 0x1234 }));
        Assert.ThrowsExactly<ArgumentException>(
            () => provider.WrapGlTexture(1, size, new GlTextureOptions { TextureTarget = 0x8513 }));
        Assert.IsFalse(provider.ReleaseGlTexture(4242), "Releasing an unwrapped id is not an error.");
    }

    [TestMethod]
    public void DefaultOptions_AreTheOrdinaryPremultipliedSrgbRgba8Texture()
    {
        var options = default(GlTextureOptions);

        Assert.AreEqual(GlTextureOptions.Texture2D, options.ResolvedTextureTarget);
        Assert.AreEqual(GlTextureOptions.Rgba8, options.ResolvedSizedFormat);
        Assert.AreEqual(GlTextureOrigin.TopLeft, options.Origin);
        Assert.AreEqual(ColorSpace.Srgb, options.ColorSpace);
        Assert.IsFalse(options.IsStraightAlpha);
    }

    [TestMethod]
    public void ARasterContextDrawsAWrappedTextureByReadingItBack_WhichIsWhatTheCapsFlagGuards()
    {
        using var fixture = GpuFixture.Require();
        using var pipeline = new AuthorGlPipeline(TextureExtent, TextureExtent);
        pipeline.Render();
        fixture.Provider.ResetGlState();
        var image = fixture.Provider.WrapGlTexture(pipeline.TextureId, pipeline.Size);

        using var raster = new RasterSkiaSurfaceProvider();
        using var target = raster.CreateTarget(new SurfaceSpec(32, 32));
        var context = target.GetContext();

        Assert.IsFalse(
            context.Caps.HasFlag(RenderCaps.GpuBacked),
            "This is the flag a drawable holding a wrapped texture validates at attach time.");

        context.Clear(Backdrop);
        context.DrawImage(image, Matrix3x2.Identity, ImageSampling.Nearest);
        using var snapshot = target.Snapshot();
        var pixels = new byte[32 * 32 * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);

        // Measured, and worth knowing: Skia does not refuse. It pulls the texture off the GPU and
        // draws the correct pixels on the CPU surface — a full texture readback, per draw, silently.
        // The attach-time capability check is therefore not protecting correctness; it is protecting
        // an author from a per-frame download that looks like nothing at all until the frame budget
        // is gone.
        AssertPixel(pixels, 4, 4, 255, 0, 0, "the wrapped texture, read back onto a CPU surface", 32);
    }

    private static byte[] DrawWrapped(GpuFixture fixture, IImage image)
    {
        using var target = fixture.Provider.CreateTarget(new SurfaceSpec(TargetWidth, TargetHeight));
        var context = target.GetContext();
        context.Clear(Backdrop);
        context.DrawImage(image, Placement, ImageSampling.Nearest);
        fixture.Provider.Flush();
        return GpuPixels.Read(target);
    }

    private static void AssertPixel(
        byte[] pixels,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        string what,
        int width = TargetWidth)
    {
        var actual = GpuPixels.At(pixels, width, x, y);
        Assert.AreEqual((r, g, b, (byte)255), actual, $"Expected {what} at ({x},{y}).");
    }

    /// <summary>Returns the next texture name GL would hand out, without keeping it.</summary>
    private static uint ProbeNextTextureName()
    {
        var probe = new uint[1];
        TestGl.glGenTextures(1, probe);
        TestGl.glDeleteTextures(1, probe);
        return probe[0];
    }
}
