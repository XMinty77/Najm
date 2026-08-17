using System.Numerics;

namespace Najm.Core.Tests.Delivery;

/// <summary>
/// A surface provider with no backend behind it: enough of the seam for the offline loop to run,
/// and nothing that allocates once it is warm.
/// </summary>
/// <remarks>
/// The offline loop's timing, ownership, and allocation contracts are backend-independent, so
/// proving them here keeps them separable from anything Skia does. Pixel truth is the Skia suite's
/// job.
/// </remarks>
internal sealed class StubSurfaceProvider : ISurfaceProvider
{
    internal int TargetsCreated { get; private set; }

    internal int CompositorsCreated { get; private set; }

    internal StubRenderTarget? LastTarget { get; private set; }

    internal StubCompositor? LastCompositor { get; private set; }

    internal bool Disposed { get; private set; }

    public IRenderTarget CreateTarget(in SurfaceSpec spec)
    {
        TargetsCreated++;
        LastTarget = new StubRenderTarget(spec);
        return LastTarget;
    }

    public ICompositor CreateCompositor()
    {
        CompositorsCreated++;
        LastCompositor = new StubCompositor();
        return LastCompositor;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>A render target whose snapshot is one reused image over a settable fill byte.</summary>
internal sealed class StubRenderTarget : IRenderTarget
{
    private readonly StubImage snapshot;

    internal StubRenderTarget(in SurfaceSpec spec)
    {
        SurfaceSpec = spec;
        Size = spec.Size;
        snapshot = new StubImage(this);
    }

    public PixelSize Size { get; }

    public SurfaceSpec SurfaceSpec { get; }

    /// <summary>Gets or sets the byte every readback fills the frame with.</summary>
    internal byte Fill { get; set; }

    internal bool Disposed { get; private set; }

    internal int SnapshotCount { get; private set; }

    public IDrawContext2D GetContext() => GetContext(1f);

    public IDrawContext2D GetContext(float renderScale) =>
        throw new NotSupportedException(
            "The stub target composites through a stub compositor and never hands out a context.");

    public IImage Snapshot()
    {
        SnapshotCount++;
        return snapshot;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>An image whose readback is a constant fill, so a frame's bytes are predictable.</summary>
internal sealed class StubImage : IImage
{
    private readonly StubRenderTarget owner;

    internal StubImage(StubRenderTarget owner) => this.owner = owner;

    public PixelSize Size => owner.Size;

    internal int DisposeCount { get; private set; }

    public void CopyPixels(Span<byte> destination, PixelFormat format)
    {
        var required = owner.Size.Width * owner.Size.Height * 4;
        if (destination.Length < required)
        {
            throw new ArgumentException("The stub readback destination is too small.", nameof(destination));
        }

        destination[..required].Fill(owner.Fill);
    }

    public void Dispose() => DisposeCount++;
}

/// <summary>A compositor that records frames instead of painting them.</summary>
internal sealed class StubCompositor : ICompositor
{
    public CompositorStats Stats => default;

    public CompositorDebugOptions Debug { get; } = new();

    internal int RenderCount { get; private set; }

    internal bool Disposed { get; private set; }

    public void Render(LayerStack layers, IRenderTarget output, in Vector2 virtualResolution, float renderScale)
    {
        RenderCount++;
        if (output is StubRenderTarget target)
        {
            // Make each composited frame distinguishable in the pixels a sink receives.
            target.Fill = unchecked((byte)RenderCount);
        }
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Records the stream a sink was driven with, keeping one byte of each frame.</summary>
internal sealed class RecordingFrameSink : IFrameSink
{
    internal FrameStreamInfo Info { get; private set; }

    internal List<long> Frames { get; } = [];

    internal List<byte> FirstBytes { get; } = [];

    internal int BeginCount { get; private set; }

    internal int EndCount { get; private set; }

    public void Begin(in FrameStreamInfo info)
    {
        BeginCount++;
        Info = info;
    }

    public void Submit(long frame, PixelFrameLease pixels)
    {
        using (pixels)
        {
            Frames.Add(frame);
            FirstBytes.Add(pixels.Pixels[0]);
        }
    }

    public void End() => EndCount++;
}

/// <summary>A sink that fails on a chosen frame, the way a dying encoder would.</summary>
internal sealed class FailingFrameSink : IFrameSink, IDisposable
{
    private readonly long failOnFrame;

    internal FailingFrameSink(long failOnFrame) => this.failOnFrame = failOnFrame;

    internal int SubmittedFrames { get; private set; }

    internal int DisposeCount { get; private set; }

    internal int EndCount { get; private set; }

    public void Begin(in FrameStreamInfo info)
    {
    }

    public void Submit(long frame, PixelFrameLease pixels)
    {
        // A correct sink owns the lease from entry, so it releases it even on the way out.
        using (pixels)
        {
            SubmittedFrames++;
            if (frame == failOnFrame)
            {
                throw new IOException($"The stub encoder died on frame {frame}.");
            }
        }
    }

    public void End() => EndCount++;

    public void Dispose() => DisposeCount++;
}

/// <summary>Measures managed allocation across the steady state of a warm offline loop.</summary>
/// <remarks>
/// <para>
/// The measurement is taken from inside the loop because a render owns its scene's whole life: the
/// warm frames and the measured frames have to belong to the same run.
/// </para>
/// <para>
/// A collection is forced during the warm-up, well before the baseline, so lazily populated runtime
/// caches are rebuilt inside the warm-up rather than mistaken for per-frame cost. It is deliberately
/// not the last thing before the baseline: <see cref="GC.Collect()"/> charges the calling thread a
/// fixed 248 bytes of its own bookkeeping to the next window, which is a constant regardless of loop
/// length and has nothing to do with the loop. <see cref="SettleFrames"/> frames separate the two so
/// that constant lands in the warm-up where it belongs.
/// </para>
/// </remarks>
internal sealed class AllocationProbeSink : IFrameSink
{
    /// <summary>Frames between the forced collection and the baseline reading.</summary>
    private const long SettleFrames = 8L;

    private readonly long warmFrames;
    private readonly long totalFrames;
    private long baseline;

    internal AllocationProbeSink(long warmFrames, long totalFrames)
    {
        this.warmFrames = warmFrames;
        this.totalFrames = totalFrames;
    }

    internal long AllocatedBytes { get; private set; } = -1L;

    internal long MeasuredFrames { get; private set; }

    public void Begin(in FrameStreamInfo info)
    {
    }

    public void Submit(long frame, PixelFrameLease pixels)
    {
        pixels.Dispose();

        if (frame == warmFrames - 1L - SettleFrames)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        else if (frame == warmFrames - 1L)
        {
            baseline = GC.GetAllocatedBytesForCurrentThread();
        }
        else if (frame == totalFrames - 1L)
        {
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - baseline;
            MeasuredFrames = totalFrames - warmFrames;
        }
    }

    public void End()
    {
    }
}

/// <summary>A scene that records the time of every tick it receives.</summary>
internal sealed class TimelineScene : Scene
{
    private readonly TimelineLayer layer;

    internal TimelineScene() => layer = Layers.Add(new TimelineLayer());

    internal List<double> Elapsed => layer.Elapsed;

    internal List<double> Deltas => layer.Deltas;

    internal List<long> Frames => layer.Frames;

    internal int StartCount { get; private set; }

    internal int LoadCount { get; private set; }

    internal int UnloadCount { get; private set; }

    protected override void OnLoad() => LoadCount++;

    protected override void OnStart() => StartCount++;

    protected override void OnUnload() => UnloadCount++;

    private sealed class TimelineLayer : ScreenLayer
    {
        internal List<double> Elapsed { get; } = [];

        internal List<double> Deltas { get; } = [];

        internal List<long> Frames { get; } = [];

        protected override void Update(in TickContext tick)
        {
            Elapsed.Add(tick.Time.Elapsed);
            Deltas.Add(tick.Time.Dt);
            Frames.Add(tick.Time.Frame);
        }
    }
}

/// <summary>A scene whose tick does the least work that is still a tick.</summary>
internal sealed class CountingScene : Scene
{
    private readonly CountingLayer layer;

    internal CountingScene() => layer = Layers.Add(new CountingLayer());

    internal long UpdateCount => layer.UpdateCount;

    internal int StartCount { get; private set; }

    protected override void OnStart() => StartCount++;

    private sealed class CountingLayer : ScreenLayer
    {
        internal CountingLayer() => Root.Add(new Node2D());

        internal long UpdateCount { get; private set; }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }
}
