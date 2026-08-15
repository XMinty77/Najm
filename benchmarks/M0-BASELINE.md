# M0 benchmark baseline

Recorded 2026-08-11 UTC. BenchmarkDotNet 0.15.8 is pinned because it is the
[current stable NuGet release](https://www.nuget.org/packages/BenchmarkDotNet/0.15.8);
0.16.0 is preview-only.

## Method and environment

Command:

```text
dotnet run --project benchmarks/Najm.Benchmarks/Najm.Benchmarks.csproj \
  -c Release --no-build --no-restore -- --filter '*' --join \
  --artifacts /tmp/najm-m0-benchmark-artifacts-20260811
```

The three benchmarks ran serially with BenchmarkDotNet's `ShortRun` job: one
launch, three warmups, and three measured iterations. `MemoryDiagnoser` was
enabled. The host was an Ubuntu 24.04.4 KVM VPS with an Intel Haswell-class CPU
at 3.10 GHz, 6 physical/logical cores, .NET SDK 10.0.110, and .NET 10.0.10 x64
RyuJIT (`x86-64-v3`, concurrent workstation GC).

| Hot path | Mean | StdDev | Managed allocation/op |
| --- | ---: | ---: | ---: |
| Concrete `Ease.InOutCubic.Evaluate(0.37f)` | 2.769 ns | 0.199 ns | none detected |
| Reserved `PathBuilder` reset plus six-command rebuild | 14.765 ns | 1.141 ns | none detected |
| Warmed CPU-Skia draw of a six-command antialiased path on 64×64 RGBA8888 | 55.866 µs | 6.558 µs | none detected |

These are directional development baselines, not stable release thresholds: the
short job has only three samples and ran on a shared virtual host. BenchmarkDotNet
could not raise child-process priority (`Permission denied`), but all benchmarks
completed. MemoryDiagnoser reports managed allocations only; it does not measure
Skia's native allocations.

State traversal is deliberately deferred until `Node` traversal exists. This M0
suite does not fabricate a traversal workload or placeholder scene graph.

## Single-target pixel storage estimates

These estimates are `width × height × bytes-per-pixel`: 4 B/px for tagged sRGB
RGBA8888 premultiplied storage and 8 B/px for tagged linear-sRGB RGBAF16
premultiplied storage. MiB uses 1,048,576 bytes.

| Target | Pixels | sRGB RGBA8888 bytes | sRGB MiB | Linear RGBAF16 bytes | Linear MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Draft 960×540 | 518,400 | 2,073,600 | 1.98 | 4,147,200 | 3.96 |
| 1080p 1920×1080 | 2,073,600 | 8,294,400 | 7.91 | 16,588,800 | 15.82 |
| 4K 3840×2160 | 8,294,400 | 33,177,600 | 31.64 | 66,355,200 | 63.28 |

The table is a lower-bound estimate for one tightly packed target. It excludes
row padding, native metadata, snapshots, layers, staging buffers, and additional
simultaneously live surfaces.
