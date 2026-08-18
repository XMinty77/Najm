using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// The bound NAJM-SKIA II.2 asks for — "caches trim … so an abandoned gradient doesn't pin GPU
/// memory forever" — realized as a capacity with least-recently-used eviction, plus the two
/// properties the bound must not cost: a stable descriptor set stays a pure cache hit, and eviction
/// disposes the native object rather than leaking it.
/// </summary>
[TestClass]
public sealed class DescriptorCacheTests
{
    [TestMethod]
    public void TheCacheHoldsItsCapacityAndEvictsTheLeastRecentlyUsedEntry()
    {
        var probe = new DisposalLog();
        var cache = new DescriptorCache<int, Tracked>(3);

        for (var key = 0; key < 3; key++)
        {
            Assert.IsFalse(cache.TryGet(key, out _));
            cache.Add(key, probe.Create(key));
        }

        Assert.AreEqual(3, cache.Count);
        Assert.AreEqual(3, cache.Capacity);
        Assert.AreEqual(0, cache.EvictionCount);

        // Touch 0, so the recency order becomes 0 (newest), 2, 1 (oldest) and the next insertion
        // must evict 1 rather than the numerically or chronologically first entry.
        Assert.IsTrue(cache.TryGet(0, out var zero));
        Assert.AreEqual(0, zero.Key);

        cache.Add(3, probe.Create(3));

        Assert.AreEqual(3, cache.Count);
        Assert.AreEqual(1, cache.EvictionCount);
        Assert.AreEqual("1", probe.Disposed, "The least recently used entry is the one evicted.");
        Assert.IsFalse(cache.TryGet(1, out _));
        Assert.IsTrue(cache.TryGet(0, out _));
        Assert.IsTrue(cache.TryGet(2, out _));
        Assert.IsTrue(cache.TryGet(3, out _));
    }

    [TestMethod]
    public void EveryEvictedAndEveryRemainingNativeObjectIsDisposed()
    {
        var probe = new DisposalLog();
        var cache = new DescriptorCache<int, Tracked>(2);

        for (var key = 0; key < 6; key++)
        {
            cache.Add(key, probe.Create(key));
        }

        // Six added into a cache of two: the four oldest are evicted in order as they are displaced.
        Assert.AreEqual(2, cache.Count);
        Assert.AreEqual(4, cache.EvictionCount);
        Assert.AreEqual("0,1,2,3", probe.Disposed);

        cache.Clear();

        Assert.AreEqual(0, cache.Count);
        Assert.AreEqual("0,1,2,3,5,4", probe.Disposed, "Clear disposes what is left, newest first.");
    }

    [TestMethod]
    public void ASingleEntryCacheStillEvictsAndNeverLosesTrackOfItsEnds()
    {
        // Capacity one is the degenerate shape where the recency list's head and tail are the same
        // node, which is where an intrusive list normally corrupts itself.
        var probe = new DisposalLog();
        var cache = new DescriptorCache<int, Tracked>(1);

        cache.Add(7, probe.Create(7));
        Assert.IsTrue(cache.TryGet(7, out _));

        cache.Add(8, probe.Create(8));

        Assert.AreEqual(1, cache.Count);
        Assert.AreEqual(1, cache.EvictionCount);
        Assert.AreEqual("7", probe.Disposed);
        Assert.IsFalse(cache.TryGet(7, out _));
        Assert.IsTrue(cache.TryGet(8, out var survivor));
        Assert.AreEqual(8, survivor.Key);
    }

    [TestMethod]
    public void AnAnimatedGradientCannotGrowTheShaderCacheWithoutBound()
    {
        // The scenario this bound exists for. The first authored sample scene is an orrery that
        // animates gradient colours around a loop; every frame's brush is a distinct Brush *value*,
        // so every frame mints a shader. Before the bound, 600 frames left 600 live SKShaders and
        // released none of them for the life of the target.
        const int Frames = 600;

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 4));
        var context = (SkiaDrawContext2D)target.GetContext();
        var path = Rectangle(0f, 0f, 4f, 4f);

        for (var frame = 0; frame < Frames; frame++)
        {
            context.DrawPath(path, Paint.Fill(AnimatedGradient(frame / (float)Frames)));
        }

        var bound = SkiaDrawContext2D.DescriptorCacheBound;

        Assert.AreEqual(
            bound,
            context.CachedShaderCount,
            $"An animated brush must leave the cache at its bound of {bound}, not at one entry per frame.");
        Assert.AreEqual(
            Frames - bound,
            context.EvictedShaderCount,
            "Every frame past the bound must evict exactly one entry.");

        // A marching dash phase is the same failure mode with a different descriptor.
        for (var frame = 0; frame < Frames; frame++)
        {
            context.DrawPath(
                path,
                Paint.Stroke(Color.White, 1f, dash: new StrokeDash([2f, 2f], frame * 0.25f)));
        }

        Assert.AreEqual(bound, context.CachedDashCount);
        Assert.AreEqual(Frames - bound, context.EvictedDashCount);
    }

    [TestMethod]
    public void AWorkingSetInsideTheBoundNeverEvicts_HoweverManyFramesItRuns()
    {
        // The other half of the bound's contract: eviction must be the animated case's cost, not
        // everybody's. A palette of distinct brushes redrawn every frame has to stay resident.
        const int PaletteSize = 16;
        const int Frames = 200;

        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 4));
        var context = (SkiaDrawContext2D)target.GetContext();
        var path = Rectangle(0f, 0f, 4f, 4f);
        var palette = new Paint[PaletteSize];
        for (var index = 0; index < PaletteSize; index++)
        {
            palette[index] = Paint.Fill(AnimatedGradient(index / (float)PaletteSize));
        }

        for (var frame = 0; frame < Frames; frame++)
        {
            for (var index = 0; index < PaletteSize; index++)
            {
                context.DrawPath(path, palette[index]);
            }
        }

        Assert.AreEqual(PaletteSize, context.CachedShaderCount);
        Assert.AreEqual(0, context.EvictedShaderCount, "A working set inside the bound must not churn.");
    }

    [TestMethod]
    public void AStableBrushAndDashStayAPureCacheHitThatAllocatesNothing()
    {
        // The zero-allocation guarantee the bound must not regress: an LRU hit is a dictionary
        // lookup plus a few reference writes to relink the entry at the head of the recency list,
        // and the entry nodes are already allocated.
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 4));
        var context = (SkiaDrawContext2D)target.GetContext();
        var path = Rectangle(0f, 0f, 4f, 4f);

        // Built once, outside the measured body: constructing a Brush copies its stop ramp, which is
        // an allocation belonging to the author's setup rather than to the frame loop.
        var filled = Paint.Fill(AnimatedGradient(0.25f));
        var stroked = Paint.Stroke(Color.White, 1f, dash: new StrokeDash([2f, 2f]));

        AllocationProbe.AssertNoneAllocated(
            5_000,
            () =>
            {
                context.DrawPath(path, filled);
                context.DrawPath(path, stroked);
            },
            "A warm draw loop over a stable brush and dash");

        Assert.AreEqual(1, context.CachedShaderCount);
        Assert.AreEqual(1, context.CachedDashCount);
        Assert.AreEqual(0, context.EvictedShaderCount);
        Assert.AreEqual(0, context.EvictedDashCount);
    }

    [TestMethod]
    public void ARejectedBrushLeavesTheCacheExactlyAsItWas()
    {
        // The factory runs before anything is evicted, so a brush the backend cannot lower must not
        // have cost a live entry its native object.
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 4));
        var context = (SkiaDrawContext2D)target.GetContext();
        var path = Rectangle(0f, 0f, 4f, 4f);
        var good = Paint.Fill(AnimatedGradient(0.5f));
        using var snapshot = target.Snapshot();

        context.DrawPath(path, good);
        Assert.AreEqual(1, context.CachedShaderCount);

        Assert.ThrowsExactly<NotSupportedException>(
            () => context.DrawPath(path, Paint.Fill(Brush.Pattern(snapshot))));

        Assert.AreEqual(1, context.CachedShaderCount);
        Assert.AreEqual(0, context.EvictedShaderCount);

        // And the surviving entry is still the same cached object, not a silently rebuilt one.
        context.DrawPath(path, good);
        Assert.AreEqual(1, context.CachedShaderCount);
        Assert.AreEqual(0, context.EvictedShaderCount);
    }

    /// <summary>A linear gradient whose stop colours depend on <paramref name="phase"/>.</summary>
    /// <remarks>
    /// Every distinct phase is a distinct <see cref="Brush"/> value and therefore a distinct cache
    /// key, which is exactly what an animated brush does to a value-keyed cache.
    /// </remarks>
    private static Brush AnimatedGradient(float phase) => Brush.Linear(
        Vector2.Zero,
        new Vector2(4f, 0f),
        [
            new GradientStop(0f, Color.Srgb(phase, 1f - phase, 0.5f)),
            new GradientStop(1f, Color.Srgb(0.5f, phase, 1f - phase)),
        ]);

    private static PathBuilder Rectangle(float x, float y, float width, float height) =>
        new PathBuilder(initialCapacity: 5)
            .MoveTo(x, y)
            .LineTo(x + width, y)
            .LineTo(x + width, y + height)
            .LineTo(x, y + height)
            .Close();

    /// <summary>Records the order in which the cache disposed the objects it owned.</summary>
    private sealed class DisposalLog
    {
        private readonly List<int> disposed = [];

        /// <summary>Gets the keys disposed so far, in order.</summary>
        internal string Disposed => string.Join(',', disposed);

        /// <summary>Creates a stand-in native object the cache will own.</summary>
        internal Tracked Create(int key) => new(key, disposed);
    }

    /// <summary>A stand-in for a native object, which notes its own disposal.</summary>
    private sealed class Tracked(int key, List<int> disposed) : IDisposable
    {
        internal int Key => key;

        public void Dispose() => disposed.Add(key);
    }
}
