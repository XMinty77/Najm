namespace Najm.Core.Tests.Timing;

[TestClass]
public sealed class TickContextTests
{
    [TestMethod]
    public void ConstructedContextUsesCanonicalEmptyInput()
    {
        var time = FixedStepTiming.Tick(0L, 60d);
        var context = new TickContext(time);

        Assert.IsTrue(context.IsValid);
        Assert.AreEqual(time, context.Time);
        Assert.IsTrue(context.Input.IsEmpty);
        Assert.IsTrue(InputBlock.Empty.IsEmpty);
        Assert.IsTrue(default(InputBlock).IsEmpty);
    }

    [TestMethod]
    public void DefaultContextIsExplicitlyInvalid()
    {
        TickContext context = default;

        Assert.IsFalse(context.IsValid);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Time);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Input);
        Assert.ThrowsExactly<ArgumentException>(() => new TickContext(default(TimeInfo)));
    }

    [TestMethod]
    public void ContextConstructionAndReadsAllocateNoManagedMemory()
    {
        var time = FixedStepTiming.Tick(0L, 60d);
        var accumulator = new TickContext(time).Time.Elapsed;
        var constructions = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            10_000,
            () =>
            {
                var context = new TickContext(time, InputBlock.Empty);
                accumulator += context.Time.Elapsed;
                _ = context.Input;
                constructions++;
            },
            "Tick context construction and reads");

        Assert.AreEqual(reading.Invocations, constructions);
        Assert.IsGreaterThan(0d, accumulator);
    }
}
