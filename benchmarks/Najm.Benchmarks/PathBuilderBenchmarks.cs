using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Najm.Core;

namespace Najm.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class PathBuilderBenchmarks
{
    private readonly PathBuilder path = new(FillRule.EvenOdd, initialCapacity: 8);

    [GlobalSetup]
    public void Setup() => RebuildPath();

    [Benchmark]
    public int ResetAndRebuildReservedPath()
    {
        path.Reset();
        RebuildPath();
        return path.Count;
    }

    private void RebuildPath() =>
        path.MoveTo(1f, 1f)
            .LineTo(31f, 2f)
            .QuadTo(38f, 12f, 29f, 24f)
            .CubicTo(22f, 31f, 8f, 30f, 3f, 19f)
            .LineTo(9f, 10f)
            .Close();
}
