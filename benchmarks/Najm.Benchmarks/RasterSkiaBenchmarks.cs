using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Najm.Core;
using Najm.Skia;
using Najm.Utils;

namespace Najm.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RasterSkiaBenchmarks
{
    private RasterSkiaSurfaceProvider provider = null!;
    private IRenderTarget target = null!;
    private SkiaDrawContext2D context = null!;
    private PathBuilder path = null!;
    private Paint paint;

    [GlobalSetup]
    public void Setup()
    {
        provider = new RasterSkiaSurfaceProvider();
        target = provider.CreateTarget(new SurfaceSpec(64, 64));
        context = (SkiaDrawContext2D)target.GetContext();
        path = new PathBuilder(FillRule.EvenOdd, initialCapacity: 8)
            .MoveTo(3f, 2f)
            .LineTo(61f, 5f)
            .QuadTo(57f, 38f, 43f, 59f)
            .CubicTo(29f, 52f, 13f, 61f, 5f, 39f)
            .LineTo(18f, 20f)
            .Close();
        paint = Paint.Fill(Color.Srgb(0.15f, 0.5f, 0.9f, 0.8f));

        context.DrawPath(path, paint);
    }

    [Benchmark]
    public void DrawWarmedPath() => context.DrawPath(path, paint);

    [GlobalCleanup]
    public void Cleanup()
    {
        target.Dispose();
        provider.Dispose();
    }
}
