using HarfBuzzSharp;
using Najm.Text.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;

namespace Najm.Text.Tests.HarfBuzz;

[TestClass]
[DoNotParallelize]
public sealed class HarfBuzzBufferPoolTests
{
    private static readonly Language English = new("en");

    [TestMethod]
    public void Return_AfterRepeatedCapacityGrowth_ReusesOneClearedNativeBufferWrapper()
    {
        using var pool = new HarfBuzzBufferPool();
        var first = pool.Rent();
        var buffer = first;

        foreach (var length in new[] { 1, 4_096, 17, 16_384, 3 })
        {
            Populate(buffer, length);
            Assert.AreEqual(length, checked((int)buffer.Length));

            pool.Return(buffer);
            buffer = pool.Rent();

            Assert.AreSame(first, buffer);
            Assert.AreEqual(0, checked((int)buffer.Length));
            Assert.AreEqual(Direction.Invalid, buffer.Direction);
            Assert.AreEqual(default, buffer.Script);
            Assert.IsTrue(string.IsNullOrEmpty(buffer.Language.Name));
        }

        pool.Return(buffer);
    }

    [TestMethod]
    public void WarmedRentReturn_DoesNotAllocateManagedWrappers_ShapedRunCreationIsOutsidePoolContract()
    {
        using var pool = new HarfBuzzBufferPool();
        var buffer = pool.Rent();
        pool.Return(buffer);

        for (var index = 0; index < 16; index++)
        {
            buffer = pool.Rent();
            pool.Return(buffer);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
        {
            buffer = pool.Rent();
            pool.Return(buffer);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, allocated);
    }

    [TestMethod]
    public void Dispose_IsIdempotent_AndRentAfterDisposeFails()
    {
        var pool = new HarfBuzzBufferPool();
        pool.Return(pool.Rent());

        pool.Dispose();
        pool.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => pool.Rent());
    }

    private static void Populate(HbBuffer buffer, int length)
    {
        buffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
        buffer.Direction = Direction.RightToLeft;
        buffer.Script = Script.Latin;
        buffer.Language = English;
        buffer.AddUtf16(new string('a', length));
    }
}
