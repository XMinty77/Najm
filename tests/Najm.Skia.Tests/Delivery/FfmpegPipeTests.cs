using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Najm.Core;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The default delivery path: raw frames piped into a real ffmpeg process, producing a real video
/// file and nothing else on disk.
/// </summary>
/// <remarks>
/// Clips here are deliberately tiny — 320×240, ten frames — and every output lives in a scratch
/// directory that is deleted whether the test passes or fails. Piping is the whole point: a PNG
/// sequence of the same clip at 4K would be gigabytes.
/// </remarks>
[TestClass]
public sealed class FfmpegPipeTests
{
    private const int Width = 320;
    private const int Height = 240;
    private const int Frames = 10;
    private const double Fps = 30d;

    [TestMethod]
    public void TheFfmpegPipeEncodesARealMp4WithTheRequestedGeometry()
    {
        using var scratch = new ScratchDirectory();
        var path = scratch.File("clip.mp4");

        using var sink = FrameSink.FfmpegPipe(path);
        var delivered = SkiaOffline.Render(
            () => new EncoderProbeScene(),
            new OfflineOptions { Sink = sink, Fps = Fps, Frames = Frames });

        Assert.AreEqual((long)Frames, delivered);
        Assert.AreEqual((long)Frames, sink.SubmittedFrames);
        Assert.IsTrue(File.Exists(path), "The encoder must leave a file behind.");

        var written = new FileInfo(path).Length;
        Assert.IsGreaterThan(
            1024L,
            written,
            $"A ten-frame 320×240 H.264 clip should be more than a kilobyte, but '{path}' is {written} bytes.");

        // The whole run went down a pipe: the only thing in the directory is the encoded video.
        CollectionAssert.AreEqual(new[] { "clip.mp4" }, Directory.GetFiles(scratch.Path).Select(Path.GetFileName).ToArray());

        Assert.IsTrue(
            sink.CommandLine.Contains("libx264", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-preset slow", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-crf 16", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-pix_fmt yuv420p", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-f rawvideo", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-pixel_format rgba", StringComparison.Ordinal),
            $"Unexpected default encode command line: {sink.CommandLine}");

        var stream = Probe(path);
        if (stream is null)
        {
            Assert.Inconclusive("ffprobe is unavailable, so the container could not be inspected.");
            return;
        }

        Assert.AreEqual(Width, stream.Value.GetProperty("width").GetInt32());
        Assert.AreEqual(Height, stream.Value.GetProperty("height").GetInt32());
        Assert.AreEqual("h264", stream.Value.GetProperty("codec_name").GetString());
        Assert.AreEqual(Fps, ParseRate(stream.Value.GetProperty("r_frame_rate").GetString()!), 1e-9d);
        Assert.AreEqual(
            Frames,
            CountFrames(stream.Value, path),
            "The encoded clip must hold exactly the frames the renderer submitted.");
    }

    [TestMethod]
    public void TheCodecIsSelectableAndProResReachesTheCommandLine()
    {
        using var scratch = new ScratchDirectory();
        var path = scratch.File("master.mov");

        using var sink = FrameSink.FfmpegPipe(
            path,
            new FfmpegPipeOptions { Codec = FfmpegVideoCodec.ProRes });
        SkiaOffline.Render(
            () => new EncoderProbeScene(),
            new OfflineOptions { Sink = sink, Fps = Fps, Frames = 4L });

        Assert.IsTrue(
            sink.CommandLine.Contains("prores_ks", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-profile:v 3", StringComparison.Ordinal) &&
            sink.CommandLine.Contains("-pix_fmt yuv422p10le", StringComparison.Ordinal),
            $"Unexpected ProRes command line: {sink.CommandLine}");
        Assert.IsGreaterThan(1024L, new FileInfo(path).Length);

        var stream = Probe(path);
        if (stream is not null)
        {
            Assert.AreEqual("prores", stream.Value.GetProperty("codec_name").GetString());
        }
    }

    [TestMethod]
    public void AMissingFfmpegFailsLoudlyAndLeavesNoLoadedScene()
    {
        using var scratch = new ScratchDirectory();
        var scene = new EncoderProbeScene();
        using var surfaces = new RasterSkiaSurfaceProvider();
        using var sink = FrameSink.FfmpegPipe(
            scratch.File("never.mp4"),
            new FfmpegPipeOptions { Executable = "najm-no-such-ffmpeg-binary" });

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = Fps, Frames = 2L }));

        Assert.IsTrue(
            failure.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) &&
            failure.Message.Contains("PATH", StringComparison.Ordinal),
            $"A missing encoder must say so plainly, but the message was '{failure.Message}'.");
        Assert.IsNotNull(failure.InnerException, "The launcher's own error must be preserved.");
        Assert.AreEqual(SceneState.Unloaded, scene.State);
        Assert.IsEmpty(Directory.GetFiles(scratch.Path), "No output may be left behind.");
    }

    [TestMethod]
    public void AnFfmpegFailureIsReportedWithItsOwnDiagnostics()
    {
        // '.najm' is not a container ffmpeg knows, so it refuses at startup. The render must fail
        // with ffmpeg's own words rather than quietly producing nothing.
        using var scratch = new ScratchDirectory();
        var scene = new EncoderProbeScene();
        using var surfaces = new RasterSkiaSurfaceProvider();
        using var sink = FrameSink.FfmpegPipe(scratch.File("broken.najm"));

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Fps = Fps, Frames = 4L }));

        Assert.IsTrue(
            failure.Message.Contains("Command: ", StringComparison.Ordinal),
            $"A failure must quote the command that produced it: '{failure.Message}'.");
        Assert.IsTrue(
            failure.Message.Contains("ffmpeg stderr (tail):", StringComparison.Ordinal),
            $"A failure must quote ffmpeg's own diagnostics: '{failure.Message}'.");
        Assert.AreEqual(SceneState.Unloaded, scene.State);
    }

    [TestMethod]
    public void OddDimensionsAreRefusedBeforeAnythingIsRendered()
    {
        // 4:2:0 chroma cannot represent an odd frame, and finding that out after the render would
        // waste the whole run.
        using var scratch = new ScratchDirectory();
        using var sink = FrameSink.FfmpegPipe(scratch.File("odd.mp4"));

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            sink.Begin(new FrameStreamInfo(321, 240, Fps, PixelFormat.Rgba8888, 10L)));

        Assert.IsTrue(
            failure.Message.Contains("even dimensions", StringComparison.Ordinal),
            $"The constraint must be explained: '{failure.Message}'.");
        Assert.IsEmpty(Directory.GetFiles(scratch.Path));
    }

    [TestMethod]
    public void APremultipliedStreamIsRefusedRatherThanSilentlyMiscolored()
    {
        using var scratch = new ScratchDirectory();
        using var sink = FrameSink.FfmpegPipe(scratch.File("premul.mp4"));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            sink.Begin(new FrameStreamInfo(Width, Height, Fps, PixelFormat.Bgra8888Premul, 10L)));
    }

    private static double ParseRate(string rational)
    {
        var slash = rational.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return double.Parse(rational, CultureInfo.InvariantCulture);
        }

        var numerator = double.Parse(rational[..slash], CultureInfo.InvariantCulture);
        var denominator = double.Parse(rational[(slash + 1)..], CultureInfo.InvariantCulture);
        return numerator / denominator;
    }

    /// <summary>
    /// Reads the frame count, preferring the container's own tally and falling back to counting
    /// packets when the muxer did not record one.
    /// </summary>
    private static int CountFrames(JsonElement stream, string path)
    {
        if (stream.TryGetProperty("nb_frames", out var declared) &&
            int.TryParse(declared.GetString(), CultureInfo.InvariantCulture, out var count))
        {
            return count;
        }

        var packets = RunProbe(
            "-v", "error", "-select_streams", "v:0", "-count_packets",
            "-show_entries", "stream=nb_read_packets", "-of", "json", path);
        using var document = JsonDocument.Parse(packets!);
        return int.Parse(
            document.RootElement.GetProperty("streams")[0].GetProperty("nb_read_packets").GetString()!,
            CultureInfo.InvariantCulture);
    }

    private static JsonElement? Probe(string path)
    {
        var json = RunProbe(
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=width,height,codec_name,r_frame_rate,nb_frames",
            "-of", "json", path);
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        return streams.GetArrayLength() == 0 ? null : streams[0].Clone();
    }

    private static string? RunProbe(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("ffprobe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
