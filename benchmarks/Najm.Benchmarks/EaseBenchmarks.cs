using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Najm.Utils;

namespace Najm.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class EaseBenchmarks
{
    private TimingFunction ease = Ease.InOutCubic;
    private float progress = 0.37f;

    [Benchmark]
    public float EvaluateConcreteBuiltIn() => ease.Evaluate(progress);
}
