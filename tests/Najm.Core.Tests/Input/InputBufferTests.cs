using System.Numerics;
using System.Text;
using Najm.Tests;

namespace Najm.Core.Tests.Input;

[TestClass]
public sealed class InputBufferTests
{
    [TestMethod]
    public void EveryKeyFitsTheSnapshotBitset()
    {
        // The buffer sizes its key bitset from Key.Menu being the highest value. A key added after
        // it would silently index past the end, so the assumption is pinned here rather than
        // discovered as an IndexOutOfRangeException on someone's keyboard.
        var buffer = new InputBuffer();
        foreach (var key in Enum.GetValues<Key>())
        {
            Assert.IsLessThanOrEqualTo(
                (int)Key.Menu,
                (int)key,
                $"Key.{key} sits above Key.Menu; InputBuffer.KeyWords must grow with it.");

            buffer.PressKey(key);
            Assert.AreEqual(key != Key.Unknown, buffer.Block.IsDown(key));
        }
    }

    [TestMethod]
    public void PointerCoordinatesAreDeliveredUnclampedAndOnlyNaNIsRefused()
    {
        var buffer = new InputBuffer();

        // Section 9.1: coordinates outside the letterbox map linearly and arrive unclamped, so an
        // off-canvas drag stays smooth. Negative and beyond-resolution are both ordinary values.
        buffer.MovePointer(0, new Vector2(-400f, -12.5f));
        Assert.AreEqual(new Vector2(-400f, -12.5f), buffer.Block.PointerPosition);

        buffer.MovePointer(0, new Vector2(9000f, 5000f));
        Assert.AreEqual(new Vector2(9000f, 5000f), buffer.Block.PointerPosition);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => buffer.MovePointer(0, new Vector2(float.NaN, 0f)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => buffer.PressPointer(0, new Vector2(0f, float.PositiveInfinity), PointerButton.Left));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => buffer.ScrollPointer(0, Vector2.Zero, new Vector2(float.NaN, 0f)));
    }

    [TestMethod]
    public void APressOrReleaseNamesExactlyOneButton()
    {
        var buffer = new InputBuffer();

        foreach (var invalid in new[]
                 {
                     PointerButton.None,
                     PointerButton.Left | PointerButton.Right,
                     (PointerButton)64,
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => buffer.PressPointer(0, Vector2.Zero, invalid));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => buffer.ReleasePointer(0, Vector2.Zero, invalid));
        }

        Assert.AreEqual(0, buffer.EventCount);
    }

    [TestMethod]
    public void AnUndefinedKeyIsRefusedRatherThanRecorded()
    {
        var buffer = new InputBuffer();

        Assert.ThrowsExactly<ArgumentException>(() => buffer.PressKey((Key)9999));
        Assert.ThrowsExactly<ArgumentException>(() => buffer.ReleaseKey((Key)9999));
        Assert.ThrowsExactly<ArgumentException>(() => buffer.Reserve((Key)9999));
        Assert.ThrowsExactly<ArgumentException>(() => buffer.Unreserve((Key)9999));
        Assert.AreEqual(0, buffer.EventCount);
    }

    [TestMethod]
    public void AReservedKeyNeverAppearsAsAnEventOrAsHeldState()
    {
        var buffer = new InputBuffer();
        Assert.IsFalse(buffer.IsReserved(Key.F1), "Core reserves nothing; the reservation is host policy.");

        buffer.Reserve(Key.F1);
        buffer.Reserve(Key.F5);

        Assert.IsTrue(buffer.IsReserved(Key.F1));
        Assert.IsFalse(buffer.PressKey(Key.F1), "A reserved press is the host's, and says so.");
        Assert.IsFalse(buffer.ReleaseKey(Key.F1));
        Assert.IsFalse(buffer.PressKey(Key.F5));

        Assert.IsTrue(buffer.PressKey(Key.F2));
        Assert.AreEqual(1, buffer.EventCount);

        var block = buffer.Block;
        Assert.IsFalse(block.IsDown(Key.F1));
        Assert.IsFalse(block.WasPressed(Key.F1));
        Assert.IsTrue(block.IsDown(Key.F2));
    }

    [TestMethod]
    public void ReservingAHeldKeyClearsItSoItCannotStayHeldForever()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.F1);
        Assert.IsTrue(buffer.Block.IsDown(Key.F1));

        // Without this, the release would be dropped by the reservation that now exists and the key
        // would read as held for the rest of the run.
        buffer.Reserve(Key.F1);
        Assert.IsFalse(buffer.Block.IsDown(Key.F1));

        buffer.Unreserve(Key.F1);
        Assert.IsFalse(buffer.IsReserved(Key.F1));
        Assert.IsTrue(buffer.PressKey(Key.F1));
        Assert.IsTrue(buffer.Block.IsDown(Key.F1));
    }

    [TestMethod]
    public void ResetStateClearsHeldInputWithoutDiscardingWhatAlreadyHappened()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.LeftShift);
        buffer.PressPointer(0, new Vector2(5f, 6f), PointerButton.Left);

        buffer.ResetState();

        var block = buffer.Block;
        Assert.AreEqual(2, block.EventCount, "What already happened, happened.");
        Assert.IsFalse(block.IsDown(Key.LeftShift));
        Assert.AreEqual(PointerButton.None, block.Buttons);
        Assert.AreEqual(Vector2.Zero, block.PointerPosition);
        Assert.IsFalse(block.IsEmpty, "Two events remain, and a block with events is never empty.");

        buffer.BeginFrame();
        Assert.IsTrue(buffer.Block.IsEmpty, "Cleared state plus a cleared event list is the empty block.");
    }

    [TestMethod]
    public void TheBufferGrowsOnDemandAndKeepsEveryEventInOrder()
    {
        var buffer = new InputBuffer(capacity: 0);
        var initial = buffer.Capacity;
        Assert.IsGreaterThanOrEqualTo(8, initial, "A zero request still gets a usable floor.");

        for (var index = 0; index < 200; index++)
        {
            buffer.MovePointer(0, new Vector2(index, index));
        }

        var block = buffer.Block;
        Assert.AreEqual(200, block.EventCount);
        Assert.IsGreaterThanOrEqualTo(200, buffer.Capacity);
        for (var index = 0; index < 200; index++)
        {
            Assert.AreEqual(new Vector2(index, index), block[index].Position);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new InputBuffer(capacity: -1));
    }

    [TestMethod]
    public void AWarmFrameOfInputAllocatesNoManagedBytes()
    {
        // Section 9.1 and section 3.6: the block is a readonly struct over pooled buffers that are
        // cleared and refilled, never reallocated. The buffer is grown once here so the measurement
        // is of the steady state rather than of the growth, which section 3.6 counts as a permitted
        // transition.
        var buffer = new InputBuffer(capacity: 64);
        var runes = new[] { new Rune('a'), new Rune('b') };

        AllocationProbe.AssertNoneAllocated(
            200,
            () =>
            {
                buffer.BeginFrame();
                buffer.MovePointer(0, new Vector2(100f, 200f));
                buffer.PressPointer(0, new Vector2(100f, 200f), PointerButton.Left);
                buffer.ScrollPointer(0, new Vector2(100f, 200f), new Vector2(0f, 1f));
                buffer.PressKey(Key.W);
                buffer.EnterText(runes[0]);
                buffer.EnterText(runes[1]);
                buffer.ReleaseKey(Key.W);
                buffer.ReleasePointer(0, new Vector2(100f, 200f), PointerButton.Left);

                var block = buffer.Block;
                Consume(block);
            },
            "A warm frame of input");
    }

    private static void Consume(in InputBlock block)
    {
        _ = block.EventCount;
        _ = block.IsEmpty;
        _ = block.PointerPosition;
        _ = block.Buttons;
        _ = block.Scroll;
        _ = block.IsDown(Key.W);
        _ = block.WasPressed(Key.W);
        _ = block.WasReleased(Key.W);
        _ = block.WasPressed(PointerButton.Left);
        _ = block.WasReleased(PointerButton.Left);

        for (var index = 0; index < block.EventCount; index++)
        {
            _ = block[index];
            _ = block.IsConsumed(index);
        }

        foreach (var rune in block.Text)
        {
            _ = rune;
        }
    }
}
