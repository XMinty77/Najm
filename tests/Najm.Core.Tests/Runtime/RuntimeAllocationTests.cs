namespace Najm.Core.Tests.Runtime;

[TestClass]
public sealed class RuntimeAllocationTests
{
    [TestMethod]
    public void WarmCleanTraversalAllocatesNoManagedMemory()
    {
        var scene = new Scene();
        var layer = scene.Layers.Add(new CountingLayer());
        var parent = layer.Root.Add(new CountingNode());
        var child = parent.Add(new CountingNode());
        var behavior = child.Behaviors.Add(new CountingBehavior());
        scene.Load();

        const int warmTicks = 64;
        for (var frame = 0; frame < warmTicks; frame++)
        {
            var tick = RuntimeTicks.At(frame);
            scene.Tick(tick);
        }

        const int measuredTicks = 100_000;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var frame = warmTicks; frame < warmTicks + measuredTicks; frame++)
        {
            var tick = RuntimeTicks.At(frame);
            scene.Tick(tick);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0L, after - before);
        Assert.AreEqual(warmTicks + measuredTicks, layer.UpdateCount);
        Assert.AreEqual(warmTicks + measuredTicks, parent.UpdateCount);
        Assert.AreEqual(warmTicks + measuredTicks, child.UpdateCount);
        Assert.AreEqual(warmTicks + measuredTicks, behavior.UpdateCount);
    }

    private sealed class CountingLayer : ScreenLayer
    {
        internal int UpdateCount { get; private set; }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }

    private sealed class CountingNode : Node2D
    {
        internal int UpdateCount { get; private set; }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }

    private sealed class CountingBehavior : Behavior
    {
        internal int UpdateCount { get; private set; }

        protected override void Update(in TickContext tick) => UpdateCount++;
    }
}
