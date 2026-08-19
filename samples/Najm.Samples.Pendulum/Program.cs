using System.Diagnostics;
using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Pendulum;

/// <summary>The delivery half of the sample: a thin driver over one scene class.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "out");
        Directory.CreateDirectory(output);

        var stills = ParseTimes(args) ?? [0.0d, 6.0d, 12.0d, 17.0d];
        var wantsVideo = !args.Contains("--stills-only", StringComparer.Ordinal);
        var wantsStills = !args.Contains("--video-only", StringComparer.Ordinal);

        if (wantsStills)
        {
            foreach (var at in stills)
            {
                var path = Path.Combine(output, $"pendulum-{at * 1000d:00000}ms.png");
                var watch = Stopwatch.StartNew();
                SkiaExport.Png(() => new PendulumScene(), path, at, Design.Fps);
                Console.WriteLine($"still  t={at,5:0.00}s  {watch.ElapsedMilliseconds,6} ms  {path}");
            }
        }

        if (wantsVideo)
        {
            var path = Path.Combine(output, "pendulum.mp4");
            var frames = (long)Math.Round(Design.ClipSeconds * Design.Fps);
            using var sink = FrameSink.FfmpegPipe(
                path,
                new FfmpegPipeOptions
                {
                    Codec = FfmpegVideoCodec.H264,
                    ConstantRateFactor = 16,
                    Preset = "slow",
                    OutputPixelFormat = "yuv420p",
                });

            var watch = Stopwatch.StartNew();
            var written = SkiaOffline.Render(
                () => new PendulumScene(),
                new OfflineOptions { Sink = sink, Fps = Design.Fps, Frames = frames });

            Console.WriteLine(
                $"video  {written} frames  {watch.Elapsed.TotalSeconds:0.0} s  {path}");
            Console.WriteLine($"       {sink.CommandLine}");
        }

        return 0;
    }

    /// <summary>Reads <c>--at 1.5,3</c> into a list of still times, or null when absent.</summary>
    private static double[]? ParseTimes(string[] args)
    {
        var index = Array.IndexOf(args, "--at");
        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        return [.. args[index + 1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))];
    }
}
