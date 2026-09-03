using System.Numerics;
using System.Text;

namespace Najm.Core.Tests.Input;

[TestClass]
public sealed class InputBlockTests
{
    [TestMethod]
    public void TheDefaultBlockIsTheCanonicalEmptyBlockDeterministicRunsCarry()
    {
        var empty = InputBlock.Empty;

        Assert.IsTrue(empty.IsEmpty);
        Assert.IsTrue(default(InputBlock).IsEmpty);
        Assert.AreEqual(0, empty.EventCount);
        Assert.AreEqual(Vector2.Zero, empty.PointerPosition);
        Assert.AreEqual(PointerButton.None, empty.Buttons);
        Assert.AreEqual(Vector2.Zero, empty.Scroll);
        Assert.IsFalse(empty.IsDown(Key.A));
        Assert.IsFalse(empty.IsDown(PointerButton.Left));
        Assert.IsFalse(empty.WasPressed(Key.A));
        Assert.IsFalse(empty.WasReleased(Key.A));
        Assert.IsFalse(empty.WasPressed(PointerButton.Left));
        Assert.IsFalse(empty.WasReleased(PointerButton.Left));

        foreach (var _ in empty.Text)
        {
            Assert.Fail("The empty block yielded a text event.");
        }
    }

    [TestMethod]
    public void AFreshBufferProducesTheEmptyBlockAndAPointerRestingSomewhereIsNotEmpty()
    {
        var buffer = new InputBuffer();
        Assert.IsTrue(buffer.Block.IsEmpty);

        buffer.MovePointer(0, new Vector2(10f, 20f));
        buffer.BeginFrame();

        // The move is gone, but the position it established is level state that survives the frame,
        // so the block is no longer the canonical empty one even with no events in it.
        var block = buffer.Block;
        Assert.AreEqual(0, block.EventCount);
        Assert.AreEqual(new Vector2(10f, 20f), block.PointerPosition);
        Assert.IsFalse(block.IsEmpty);
    }

    [TestMethod]
    public void EventsArriveInPushOrderWithKindsInterleaved()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.A);
        buffer.PressPointer(1, new Vector2(4f, 5f), PointerButton.Left);
        buffer.EnterText(new Rune('a'));
        buffer.ReleaseKey(Key.A);

        var block = buffer.Block;
        Assert.AreEqual(4, block.EventCount);
        Assert.AreEqual(InputEventKind.KeyDown, block[0].Kind);
        Assert.AreEqual(InputEventKind.PointerDown, block[1].Kind);
        Assert.AreEqual(InputEventKind.Text, block[2].Kind);
        Assert.AreEqual(InputEventKind.KeyUp, block[3].Kind);

        Assert.AreEqual(1, block[1].PointerId);
        Assert.AreEqual(new Vector2(4f, 5f), block[1].Position);
        Assert.AreEqual(PointerButton.Left, block[1].Button);
        Assert.AreEqual(new Rune('a'), block[2].Text);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = block[4]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = block[-1]);
    }

    [TestMethod]
    public void APressCarriesTheButtonItAddsAndAReleaseCarriesTheSetWithoutIt()
    {
        var buffer = new InputBuffer();
        buffer.PressPointer(0, Vector2.Zero, PointerButton.Left);
        buffer.PressPointer(0, Vector2.Zero, PointerButton.Right);
        buffer.MovePointer(0, new Vector2(1f, 1f));
        buffer.ReleasePointer(0, new Vector2(1f, 1f), PointerButton.Left);

        var block = buffer.Block;
        Assert.AreEqual(PointerButton.Left, block[0].Buttons);
        Assert.AreEqual(PointerButton.Left | PointerButton.Right, block[1].Buttons);
        Assert.AreEqual(PointerButton.Left | PointerButton.Right, block[2].Buttons);
        Assert.AreEqual(PointerButton.Right, block[3].Buttons);

        Assert.AreEqual(PointerButton.Right, block.Buttons);
        Assert.IsTrue(block.IsDown(PointerButton.Right));
        Assert.IsFalse(block.IsDown(PointerButton.Left));
        Assert.IsFalse(block.IsDown(PointerButton.None));
        Assert.IsFalse(block.IsDown(PointerButton.Left | PointerButton.Right));
    }

    [TestMethod]
    public void HeldStateSurvivesBeginFrameAndEventsDoNot()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.W);
        buffer.PressPointer(0, new Vector2(3f, 3f), PointerButton.Left);

        Assert.AreEqual(2, buffer.Block.EventCount);
        Assert.IsTrue(buffer.Block.WasPressed(Key.W));

        buffer.BeginFrame();

        var block = buffer.Block;
        Assert.AreEqual(0, block.EventCount);
        Assert.IsTrue(block.IsDown(Key.W), "A key held across frames stays down.");
        Assert.IsTrue(block.IsDown(PointerButton.Left));
        Assert.IsFalse(block.WasPressed(Key.W), "The press edge belongs to the frame it happened in.");
    }

    [TestMethod]
    public void AutoRepeatIsDownButIsNotAFreshPress()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.Backspace, isRepeat: true);

        var block = buffer.Block;
        Assert.IsTrue(block.IsDown(Key.Backspace));
        Assert.IsTrue(block[0].IsRepeat);
        Assert.IsFalse(block.WasPressed(Key.Backspace));

        buffer.BeginFrame();
        buffer.PressKey(Key.Backspace);
        Assert.IsTrue(buffer.Block.WasPressed(Key.Backspace));
        Assert.IsFalse(buffer.Block[0].IsRepeat);
    }

    [TestMethod]
    public void ConsumptionHidesAnEventFromEveryPollingAccessorButNotFromTheEventList()
    {
        var buffer = new InputBuffer();
        buffer.PressPointer(0, Vector2.Zero, PointerButton.Left);
        buffer.ScrollPointer(0, Vector2.Zero, new Vector2(0f, 3f));
        buffer.PressKey(Key.Space);
        buffer.EnterText(new Rune('x'));

        var block = buffer.Block;
        Assert.IsTrue(block.WasPressed(PointerButton.Left));
        Assert.AreEqual(new Vector2(0f, 3f), block.Scroll);
        Assert.IsTrue(block.WasPressed(Key.Space));
        Assert.AreEqual(1, CountText(block));

        for (var index = 0; index < block.EventCount; index++)
        {
            block.Consume(index);
        }

        Assert.AreEqual(4, block.EventCount, "Consumption hides events from polling; it does not remove them.");
        Assert.IsTrue(block.IsConsumed(0));
        Assert.IsFalse(block.WasPressed(PointerButton.Left));
        Assert.AreEqual(Vector2.Zero, block.Scroll);
        Assert.IsFalse(block.WasPressed(Key.Space));
        Assert.AreEqual(0, CountText(block));

        Assert.IsTrue(
            block.IsDown(PointerButton.Left),
            "Consumption is per event; the level snapshot is not an event and is unaffected.");
        Assert.IsTrue(block.IsDown(Key.Space));
    }

    [TestMethod]
    public void ScrollAccumulatesEveryUnconsumedScrollEvent()
    {
        var buffer = new InputBuffer();
        buffer.ScrollPointer(0, Vector2.Zero, new Vector2(1f, 2f));
        buffer.ScrollPointer(0, Vector2.Zero, new Vector2(0.5f, -0.25f));
        buffer.ScrollPointer(0, Vector2.Zero, new Vector2(10f, 10f));

        var block = buffer.Block;
        Assert.AreEqual(new Vector2(11.5f, 11.75f), block.Scroll);

        block.Consume(2);
        Assert.AreEqual(new Vector2(1.5f, 1.75f), block.Scroll);
    }

    [TestMethod]
    public void TextWalksUnconsumedRunesInOrderIncludingAstralCharacters()
    {
        var buffer = new InputBuffer();
        buffer.EnterText(new Rune('n'));
        buffer.PressKey(Key.A);
        buffer.EnterText(Rune.GetRuneAt("\U0001D6D1", 0));
        buffer.EnterText(new Rune('m'));

        var block = buffer.Block;
        var seen = new List<Rune>();
        foreach (var rune in block.Text)
        {
            seen.Add(rune);
        }

        CollectionAssert.AreEqual(
            new[] { new Rune('n'), Rune.GetRuneAt("\U0001D6D1", 0), new Rune('m') },
            seen);
        Assert.AreEqual(0x1D6D1, seen[1].Value, "An astral character is one event, not a surrogate pair.");

        block.Consume(2);
        seen.Clear();
        foreach (var rune in block.Text)
        {
            seen.Add(rune);
        }

        CollectionAssert.AreEqual(new[] { new Rune('n'), new Rune('m') }, seen);
    }

    [TestMethod]
    public void UnknownKeyAndNoButtonAnswerFalseEverywhere()
    {
        var buffer = new InputBuffer();
        buffer.PressKey(Key.A);
        var block = buffer.Block;

        Assert.IsFalse(block.IsDown(Key.Unknown));
        Assert.IsFalse(block.WasPressed(Key.Unknown));
        Assert.IsFalse(block.WasReleased(Key.Unknown));
        Assert.IsFalse(block.WasPressed(PointerButton.None));
        Assert.IsFalse(block.WasReleased(PointerButton.None));
    }

    [TestMethod]
    public void EventsReportWhichFamilyTheyBelongTo()
    {
        var buffer = new InputBuffer();
        buffer.MovePointer(0, Vector2.Zero);
        buffer.PressPointer(0, Vector2.Zero, PointerButton.Left);
        buffer.ReleasePointer(0, Vector2.Zero, PointerButton.Left);
        buffer.ScrollPointer(0, Vector2.Zero, Vector2.UnitY);
        buffer.PressKey(Key.A);
        buffer.ReleaseKey(Key.A);
        buffer.EnterText(new Rune('a'));

        var block = buffer.Block;
        for (var index = 0; index < 4; index++)
        {
            Assert.IsTrue(block[index].IsPointerEvent, $"Event {index} should be a pointer event.");
            Assert.IsFalse(block[index].IsKeyboardEvent);
        }
        for (var index = 4; index < 7; index++)
        {
            Assert.IsTrue(block[index].IsKeyboardEvent, $"Event {index} should be a keyboard event.");
            Assert.IsFalse(block[index].IsPointerEvent);
        }

        Assert.IsFalse(default(InputEvent).IsPointerEvent);
        Assert.IsFalse(default(InputEvent).IsKeyboardEvent);
        Assert.AreEqual(InputEventKind.None, default(InputEvent).Kind);
        Assert.AreEqual(default, default(InputEvent).Text);
    }

    private static int CountText(in InputBlock block)
    {
        var total = 0;
        foreach (var _ in block.Text)
        {
            total++;
        }

        return total;
    }
}
