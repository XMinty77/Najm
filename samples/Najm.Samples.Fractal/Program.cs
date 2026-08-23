using System.Diagnostics;
using System.Globalization;
using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Fractal;

/// <summary>The delivery half of the sample: one GL context, one provider, three modes.</summary>
/// <remarks>
/// <para>
/// <c>still</c> and <c>video</c> are the deliverables. <c>probe</c> is the working loop — it parks
/// the camera at an explicit place in the set so a palette or a depth can be looked at rather than
/// reasoned about, which on a software rasterizer is the difference between a minute and an hour.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(Argument(args, "--out") ?? "out");
        Directory.CreateDirectory(output);
        var samples = int.Parse(Argument(args, "--samples") ?? "4", CultureInfo.InvariantCulture);
        var mode = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "video";

        using var gpu = GpuOffline.Create();
        Console.WriteLine($"gl     {gpu.Description}");
        Console.WriteLine($"aa     {samples}x in-shader");

        return mode switch
        {
            "probe" => Probe(gpu, args, output, samples),
            "still" => Stills(gpu, args, output, samples),
            "video" => Video(gpu, args, output, samples),
            _ => Fail($"Unknown mode '{mode}'. Use probe, still, or video."),
        };
    }

    /// <summary>Renders one frame at an explicit place in the set, bypassing the flight.</summary>
    private static int Probe(GpuOffline gpu, string[] args, string output, int samples)
    {
        var uniforms = new FractalUniforms
        {
            CentreX = Number(args, "--cx", -0.74364386269d),
            CentreY = Number(args, "--cy", 0.13182590271d),
            Scale = Number(args, "--scale", 1.3d),
            Rotation = Number(args, "--rot", 0d),
            MaxIterations = (float)Number(args, "--iter", 400d),
            PaletteShift = (float)Number(args, "--shift", 0.585d),
            Bands = (float)Number(args, "--bands", 0.545d),
            NuFloor = (float)Number(args, "--floor", 0d),
            RimGain = (float)Number(args, "--rim", 0.62d),
            FrontGain = (float)Number(args, "--front", 0.34d),
            Exposure = (float)Number(args, "--exposure", 1.06d),
        };

        var path = Path.Combine(output, (Argument(args, "--name") ?? "probe") + ".png");
        var watch = Stopwatch.StartNew();
        gpu.RenderStill(() => new FractalScene(new FixedFlight(uniforms), samples), path, at: 0d, Design.Fps, sampleCount: 1);
        Console.WriteLine(
            $"probe  scale={uniforms.Scale:0.###e+00}  iter={uniforms.MaxIterations:0}  "
            + $"{watch.Elapsed.TotalSeconds:0.00} s  {path}");
        return 0;
    }

    /// <summary>Renders the flight's stills at chosen times.</summary>
    private static int Stills(GpuOffline gpu, string[] args, string output, int samples)
    {
        var times = (Argument(args, "--at") ?? "0.9,3.6,6.0,7.2,8.25,9.3,11.4,12.9")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture));

        var flight = new Flight();
        foreach (var at in times)
        {
            var path = Path.Combine(output, $"fractal-{at * 1000d:00000}ms.png");
            var watch = Stopwatch.StartNew();

            // Deliberately NOT `new FractalScene(flight, ...)` seeked with OfflineRenderer's own
            // still path. That path is `ceil(at * fps)` ticks and then one render, and this scene's
            // tick runs the whole shader — so a still at 12.9 s would run 774 full fractal passes to
            // throw 773 of them away. The flight is a pure function of time, so evaluating it here
            // and parking the scene on that one frame is the same picture in one pass. NOTES.md F-13.
            gpu.RenderStill(
                () => new FractalScene(new FixedFlight(flight.At(at)), samples),
                path,
                at: 0d,
                Design.Fps,
                sampleCount: 1);
            Console.WriteLine($"still  t={at,6:0.00}s  {watch.Elapsed.TotalSeconds,7:0.00} s  {path}");
        }

        return 0;
    }

    /// <summary>Renders the whole clip straight into an ffmpeg pipe.</summary>
    private static int Video(GpuOffline gpu, string[] args, string output, int samples)
    {
        var path = Path.Combine(output, (Argument(args, "--name") ?? "fractal") + ".mp4");
        var seconds = Number(args, "--seconds", Design.ClipSeconds);
        var frames = (long)Math.Round(seconds * Design.Fps);

        using var sink = FrameSink.FfmpegPipe(
            path,
            new FfmpegPipeOptions
            {
                Codec = FfmpegVideoCodec.H264,
                ConstantRateFactor = 15,
                Preset = "slow",
                OutputPixelFormat = "yuv420p",
            });

        var watch = Stopwatch.StartNew();
        var written = gpu.Render(
            () => new FractalScene(new Flight(), samples),
            new OfflineOptions
            {
                Sink = new ProgressSink(sink, frames, watch),
                Fps = Design.Fps,
                Frames = frames,
            });

        Console.WriteLine($"video  {written} frames  {watch.Elapsed.TotalSeconds:0.0} s  {path}");
        Console.WriteLine($"       {sink.CommandLine}");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Number(string[] args, string name, double fallback) =>
        Argument(args, name) is { } text
            ? double.Parse(text, CultureInfo.InvariantCulture)
            : fallback;
}

/// <summary>Reports progress on the way past, because a software-rasterized clip takes a while.</summary>
/// <remarks>
/// A decorating <see cref="IFrameSink"/> rather than a hook in the loop, which is the shape the
/// interface invites: <c>Submit</c> transfers ownership of the lease to the sink, so the decorator
/// must hand it straight on and touch nothing afterwards.
/// </remarks>
internal sealed class ProgressSink(IFrameSink inner, long total, Stopwatch watch) : IFrameSink, IDisposable
{
    /// <inheritdoc />
    public void Begin(in FrameStreamInfo info) => inner.Begin(info);

    /// <inheritdoc />
    public void Submit(long frame, PixelFrameLease pixels)
    {
        inner.Submit(frame, pixels);

        if (frame % 15L == 0L || frame == total - 1L)
        {
            var done = frame + 1L;
            var perFrame = watch.Elapsed.TotalSeconds / done;
            Console.WriteLine(
                $"       frame {done,4}/{total}  {perFrame:0.00} s/frame  "
                + $"eta {TimeSpan.FromSeconds(perFrame * (total - done)):hh\\:mm\\:ss}");
        }
    }

    /// <inheritdoc />
    public void End() => inner.End();

    /// <inheritdoc />
    public void Dispose() => (inner as IDisposable)?.Dispose();
}
