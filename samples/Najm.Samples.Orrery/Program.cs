using System.Diagnostics;
using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Orrery;

/// <summary>
/// The delivery half of the sample: a thin driver over one scene class, which is the shape
/// ARCHITECTURE section 2.5 asks for.
/// </summary>
internal static class Program
{
    private const int Fps = 60;

    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "out");
        Directory.CreateDirectory(output);

        var stills = ParseTimes(args) ?? [0.0d, 2.5d, 5.0d, 7.5d, 10.0d, 12.5d];
        var wantsVideo = !args.Contains("--stills-only", StringComparer.Ordinal);
        var wantsStills = !args.Contains("--video-only", StringComparer.Ordinal);

        if (wantsStills)
        {
            foreach (var at in stills)
            {
                var path = Path.Combine(output, $"orrery-{at * 1000d:00000}ms.png");
                var watch = Stopwatch.StartNew();
                SkiaExport.Png(() => new OrreryScene(), path, at, Fps);
                Console.WriteLine($"still  t={at,5:0.00}s  {watch.ElapsedMilliseconds,6} ms  {path}");
            }
        }

        if (wantsVideo)
        {
            var path = Path.Combine(output, "orrery.mp4");
            var frames = (long)Math.Round(Design.LoopSeconds * Fps);
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
                () => new OrreryScene(),
                new OfflineOptions { Sink = sink, Fps = Fps, Frames = frames });

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
