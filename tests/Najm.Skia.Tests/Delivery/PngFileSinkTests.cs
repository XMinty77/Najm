using Najm.Core;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The still sink, now that it is reachable. It was internal, so the only public route from a render
/// to a named PNG was a numbered sequence into a scratch directory followed by a rename — and every
/// project that drove <see cref="OfflineRenderer.RenderStill"/> itself wrote that shuffle.
/// </summary>
/// <remarks>
/// Two properties are worth pinning beyond "it writes the file": it refuses a stream that is not one
/// frame long, and it refuses to report a finished stream over a file that was never written. Both
/// existed before and neither was reachable to test.
/// </remarks>
[TestClass]
public sealed class PngFileSinkTests
{
    [TestMethod]
    public void ItWritesTheStillToTheNamedPath_WhichIsWhatTheScratchDirectoryDanceWasFor()
    {
        using var scratch = new ScratchDirectory();
        var throughSink = scratch.File("nested/through-sink.png");
        var throughConvenience = scratch.File("through-convenience.png");

        using var surfaces = new RasterSkiaSurfaceProvider();
        var sink = FrameSink.PngFile(throughSink);
        var ticks = OfflineRenderer.RenderStill(
            new WalkingPixelScene(),
            surfaces,
            sink,
            at: 0.5d,
            framesPerSecond: 60d);
        SkiaExport.Png(() => new WalkingPixelScene(), throughConvenience, at: 0.5d, framesPerSecond: 60d);

        Assert.AreEqual(30L, ticks);
        Assert.IsTrue(File.Exists(throughSink), "The sink creates the directory it was pointed into.");
        Assert.IsTrue(
            FrameProbe.AreIdentical(throughSink, throughConvenience),
            "Driving the loop by hand must produce the same frame the convenience does.");
    }

    [TestMethod]
    public void ThePathItReportsIsAbsolute_AndIsResolvedWhenTheSinkIsMade()
    {
        using var scratch = new ScratchDirectory();
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), scratch.File("relative.png"));

        var sink = FrameSink.PngFile(relative);

        Assert.IsTrue(Path.IsPathRooted(sink.Path));
        Assert.AreEqual(Path.GetFullPath(relative), sink.Path);
    }

    [TestMethod]
    public void AStreamLongerThanOneFrameIsRefusedAtBegin_NotOverwrittenPerFrame()
    {
        using var scratch = new ScratchDirectory();
        var path = scratch.File("sequence.png");
        var sink = FrameSink.PngFile(path);

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => sink.Begin(new FrameStreamInfo(8, 4, 60d, PixelFormat.Rgba8888, frameCount: 24L)));

        StringAssert.Contains(refusal.Message, nameof(FrameSink.PngSequence));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void AStreamThatSubmittedNothingIsNotReportedAsFinished()
    {
        // The failure this prevents is the quiet one: End() returning normally over a path that does
        // not exist, so the caller believes it exported a still and finds out later.
        using var scratch = new ScratchDirectory();
        var path = scratch.File("empty.png");
        var sink = FrameSink.PngFile(path);

        sink.Begin(new FrameStreamInfo(8, 4, 60d, PixelFormat.Rgba8888, frameCount: 1L));
        var refusal = Assert.ThrowsExactly<InvalidOperationException>(sink.End);

        StringAssert.Contains(refusal.Message, path);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void APathThatNamesNothingIsRefusedWhereTheCallerCanSeeIt()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => FrameSink.PngFile(null!));
        Assert.ThrowsExactly<ArgumentException>(() => FrameSink.PngFile("   "));
    }
}
