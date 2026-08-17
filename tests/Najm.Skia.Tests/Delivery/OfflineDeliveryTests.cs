using Najm.Core;
using SkiaSharp;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The offline delivery slice end to end over real Skia: deterministic runs, a PNG still whose
/// pixels are derived from the timing contract, and the numbered-sequence sink.
/// </summary>
[TestClass]
public sealed class OfflineDeliveryTests
{
    private const string Black = "000000ff";
    private const string Red = "ff0000ff";

    [TestMethod]
    public void TwoFreshInstanceRunsProduceByteIdenticalFrames()
    {
        // §2.2 fresh-instance determinism: the factory is the point. Both runs load, tick, and
        // render their own instance with the canonical empty input block, so equal digests mean the
        // pipeline carries no state between runs and reads nothing outside the simulated clock.
        var first = new HashingFrameSink();
        var second = new HashingFrameSink();

        var firstFrames = SkiaOffline.Render(
            () => new WalkingPixelScene(),
            new OfflineOptions { Sink = first, Fps = 60d, Frames = 24L });
        var secondFrames = SkiaOffline.Render(
            () => new WalkingPixelScene(),
            new OfflineOptions { Sink = second, Fps = 60d, Frames = 24L });

        Assert.AreEqual(24L, firstFrames);
        Assert.AreEqual(24L, secondFrames);
        CollectionAssert.AreEqual(
            first.Hashes,
            second.Hashes,
            "Two fresh-instance runs of one scene must produce identical raw frame hashes.");

        // The clip must actually move, or identical hashes would prove nothing: the walker wraps
        // every eight frames, so 24 frames hold exactly eight distinct images repeated three times.
        Assert.AreEqual(8, first.Hashes.Distinct().Count(), "The fixture animates on an eight-frame cycle.");
        Assert.AreEqual(first.Hashes[0], first.Hashes[8]);
        Assert.AreEqual(first.Hashes[3], first.Hashes[19]);
        Assert.AreNotEqual(first.Hashes[0], first.Hashes[1]);
    }

    [TestMethod]
    public void ADeterministicRunDeliversOneFrameOfTheDeclaredShapePerTick()
    {
        var sink = new HashingFrameSink();

        SkiaOffline.Render(
            () => new WalkingPixelScene(),
            new OfflineOptions { Sink = sink, Fps = 24d, Frames = 3L, Scale = 2f });

        Assert.AreEqual(1, sink.BeginCount);
        Assert.AreEqual(1, sink.EndCount);
        Assert.HasCount(3, sink.Hashes);
        Assert.AreEqual(16, sink.Info.Width, "An 8-unit virtual width at scale 2 is 16 pixels.");
        Assert.AreEqual(8, sink.Info.Height);
        Assert.AreEqual(24d, sink.Info.FramesPerSecond);
        Assert.AreEqual(3L, sink.Info.FrameCount);
        Assert.AreEqual(PixelFormat.Rgba8888, sink.Info.Format);
    }

    [TestMethod]
    public void APngStillAtZeroShowsTheLoadedStateAndNeverRunsOnStart()
    {
        // at: 0 is zero ticks (PLAN resolution 1). The walker has never updated, so it sits at
        // column 0 — and OnStart, which would have parked it in column 7, has not run.
        using var scratch = new ScratchDirectory();
        var path = scratch.File("still-zero.png");

        var ticks = SkiaExport.Png(() => new WalkingPixelScene(), path, at: 0d, framesPerSecond: 60d);

        Assert.AreEqual(0L, ticks);
        AssertIsPng(path, expectedWidth: 8, expectedHeight: 4);
        Assert.AreEqual(ExpectedFrameWithWalkerAt(0), DecodeRgbaHex(path));
    }

    [TestMethod]
    public void APngStillAtHalfASecondShowsTheThirtiethTicksFrame()
    {
        // ceil(0.5 × 60) = 30 ticks, so the last tick is frame 29 and the walker sits at
        // 29 mod 8 = column 5. One render follows those ticks; nothing in between is written.
        using var scratch = new ScratchDirectory();
        var path = scratch.File("still-half.png");

        var ticks = SkiaExport.Png(() => new WalkingPixelScene(), path, at: 0.5d, framesPerSecond: 60d);

        Assert.AreEqual(30L, ticks);
        AssertIsPng(path, expectedWidth: 8, expectedHeight: 4);
        Assert.AreEqual(ExpectedFrameWithWalkerAt(5), DecodeRgbaHex(path));
    }

    [TestMethod]
    public void APngStillHonorsTheRenderScale()
    {
        // The same 8×4 scene at scale 4 is a 32×16 file; scale is a driver parameter and the scene
        // is unaware of it.
        using var scratch = new ScratchDirectory();
        var path = scratch.File("still-scaled.png");

        SkiaExport.Png(() => new WalkingPixelScene(), path, at: 0d, framesPerSecond: 60d, scale: 4f);

        AssertIsPng(path, expectedWidth: 32, expectedHeight: 16);
    }

    [TestMethod]
    public void ThePngSequenceSinkWritesZeroPaddedNumberedFrames()
    {
        using var scratch = new ScratchDirectory();
        var sink = FrameSink.PngSequence(scratch.Path, "clip");

        var frames = SkiaOffline.Render(
            () => new WalkingPixelScene(),
            new OfflineOptions { Sink = sink, Fps = 60d, Frames = 3L });

        Assert.AreEqual(3L, frames);
        Assert.AreEqual(3L, sink.WrittenFrames);

        var written = Directory.GetFiles(scratch.Path).Select(Path.GetFileName).Order().ToArray();
        CollectionAssert.AreEqual(
            new[] { "clip_00000.png", "clip_00001.png", "clip_00002.png" },
            written,
            "Frames are numbered from zero and padded to five digits.");

        // Output frame k is the render after tick k, so file k shows the walker at column k.
        for (var frame = 0; frame < 3; frame++)
        {
            var path = sink.PathForFrame(frame);
            AssertIsPng(path, expectedWidth: 8, expectedHeight: 4);
            Assert.AreEqual(
                ExpectedFrameWithWalkerAt(frame),
                DecodeRgbaHex(path),
                $"Frame {frame} must show the state produced by tick {frame}.");
        }
    }

    [TestMethod]
    public void ThePngSequenceSinkRejectsAFrameThatDisagreesWithItsStream()
    {
        using var scratch = new ScratchDirectory();
        var sink = FrameSink.PngSequence(scratch.Path, "mismatch");
        sink.Begin(new FrameStreamInfo(4, 4, 60d, PixelFormat.Rgba8888, 1L));

        var lease = PixelFrameLease.Rent(8, 8, PixelFormat.Rgba8888);
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => sink.Submit(0L, lease));

        Assert.IsTrue(
            failure.Message.Contains("8×8", StringComparison.Ordinal),
            $"The mismatch must be reported concretely, but the message was '{failure.Message}'.");
        Assert.ThrowsExactly<ObjectDisposedException>(lease.Dispose);
        Assert.IsEmpty(Directory.GetFiles(scratch.Path));
    }

    /// <summary>
    /// The 8×4 frame with the walker's one red pixel in <paramref name="column"/> of the top row and
    /// an opaque black clear everywhere else.
    /// </summary>
    private static string ExpectedFrameWithWalkerAt(int column)
    {
        var pixels = new string[8 * 4];
        Array.Fill(pixels, Black);
        pixels[column] = Red;
        return string.Concat(pixels);
    }

    private static void AssertIsPng(string path, int expectedWidth, int expectedHeight)
    {
        Assert.IsTrue(File.Exists(path), $"'{path}' was not written.");

        var bytes = File.ReadAllBytes(path);
        Assert.IsGreaterThan(8, bytes.Length, $"'{path}' is too short to be a PNG.");

        // The PNG signature, then the IHDR chunk's big-endian width and height at offsets 16 and 20.
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            bytes[..8],
            $"'{path}' does not start with the PNG signature.");
        Assert.AreEqual("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.AreEqual(expectedWidth, ReadBigEndianInt32(bytes, 16));
        Assert.AreEqual(expectedHeight, ReadBigEndianInt32(bytes, 20));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static string DecodeRgbaHex(string path)
    {
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Skia could not decode '{path}'.");

        var hex = new System.Text.StringBuilder(bitmap.Width * bitmap.Height * 8);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                hex.Append(color.Red.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(color.Green.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(color.Blue.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(color.Alpha.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return hex.ToString();
    }
}
