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
        scene.Load(TestEnvironment.Stub());

        const int warmTicks = 64;
        for (var frame = 0; frame < warmTicks; frame++)
        {
            var tick = RuntimeTicks.At(frame);
            scene.Tick(tick);
        }

        // The probe runs the body extra times — warm, settle, and once more per retried window — so
        // the tick count the counters must match is the probe's own total, not a constant.
        var ticked = warmTicks;
        var reading = AllocationProbe.AssertNoneAllocated(
            100_000,
            () =>
            {
                var tick = RuntimeTicks.At(ticked);
                scene.Tick(tick);
                ticked++;
            },
            "The warm clean traversal");

        Assert.AreEqual(warmTicks + reading.Invocations, ticked);
        Assert.AreEqual(ticked, layer.UpdateCount);
        Assert.AreEqual(ticked, parent.UpdateCount);
        Assert.AreEqual(ticked, child.UpdateCount);
        Assert.AreEqual(ticked, behavior.UpdateCount);
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
