using System.Numerics;
using System.Text;

namespace Najm.Core;

/// <summary>Contains input delivered for one tick: an ordered event list plus level snapshots.</summary>
/// <remarks>
/// <para>
/// <strong>A view, not a container.</strong> ARCHITECTURE §4.3 and §9.1 both require this to be a
/// <c>readonly struct</c> over per-frame <em>pooled</em> buffers that are cleared and refilled and
/// never reallocated, passed by <c>in</c>. The buffers belong to an <see cref="InputBuffer"/> the
/// host owns for the process lifetime; a block is three references and two small values over them,
/// so building one, copying one, and reading one all allocate nothing. The consequence to know: a
/// block is <strong>valid only for the tick it was handed to</strong>. Stashing one and reading it
/// next frame reads next frame's events, because the array underneath was refilled in place.
/// </para>
/// <para>
/// <strong>Events and snapshots answer different questions.</strong> The event list is every
/// discrete thing that happened since the last tick, in the order it happened, and it is what the
/// router dispatches (§9.2). The snapshots — <see cref="PointerPosition"/>,
/// <see cref="Buttons"/>, <see cref="IsDown(Key)"/> — are level state: what is true right now. A
/// scene that asks "is W held" polls; a scene that asks "did the user click that circle" routes.
/// §9.3 calls the pair hybrid routing by design.
/// </para>
/// <para>
/// <strong>Consumption.</strong> Events carry a consumed flag alongside them, set by the router when
/// a node handles one. Every polling accessor here reads the <em>unconsumed</em> events only (§9.1),
/// so a scene polling for a click does not also see the click a button already swallowed. Polling
/// never consumes and cannot capture (§9.3): reading is free of side effects, and anything
/// drag-shaped belongs on the router.
/// </para>
/// <para>
/// <strong>The empty block is the determinism contract.</strong> <c>default(InputBlock)</c> equals
/// <see cref="Empty"/>, has no events and default snapshots, and is what every deterministic
/// fixed-step driver supplies (§2.1, §2.5, Appendix A.1). <see cref="Scene.Tick"/> refuses a
/// fixed-step tick carrying anything else, so "deterministic runs take no input" is enforced rather
/// than merely documented.
/// </para>
/// </remarks>
public readonly struct InputBlock
{
    private readonly InputEvent[]? events;
    private readonly bool[]? consumed;
    private readonly ulong[]? keys;
    private readonly int count;
    private readonly Vector2 pointerPosition;
    private readonly PointerButton buttons;

    internal InputBlock(
        InputEvent[] events,
        bool[] consumed,
        ulong[] keys,
        int count,
        Vector2 pointerPosition,
        PointerButton buttons)
    {
        this.events = events;
        this.consumed = consumed;
        this.keys = keys;
        this.count = count;
        this.pointerPosition = pointerPosition;
        this.buttons = buttons;
    }

    /// <summary>Gets the canonical allocation-free empty input block.</summary>
    /// <remarks>
    /// This is what a deterministic run carries every tick, and it is exactly
    /// <c>default(InputBlock)</c> — a scene cannot tell the two apart, and no driver needs to hold
    /// a buffer in order to produce one.
    /// </remarks>
    public static InputBlock Empty => default;

    /// <summary>Gets whether this block has no events and no non-default snapshot state.</summary>
    /// <remarks>
    /// A frame in which the pointer merely rests somewhere is <em>not</em> empty: the position is
    /// live state a scene can poll. Emptiness is the deterministic-run contract (§9.1), not an
    /// "is there anything new" test — for that, ask <see cref="EventCount"/>.
    /// </remarks>
    public bool IsEmpty =>
        count == 0 &&
        buttons == PointerButton.None &&
        pointerPosition == Vector2.Zero &&
        !AnyKeyDown;

    /// <summary>Gets the number of events in this block, consumed and unconsumed alike.</summary>
    public int EventCount => count;

    /// <summary>Gets one event by its position in arrival order.</summary>
    /// <param name="index">A zero-based index below <see cref="EventCount"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the block.</exception>
    public InputEvent this[int index]
    {
        get
        {
            EnsureIndex(index);
            return events![index];
        }
    }

    /// <summary>Gets whether the event at an index has already been handled by a routed node.</summary>
    /// <param name="index">A zero-based index below <see cref="EventCount"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the block.</exception>
    public bool IsConsumed(int index)
    {
        EnsureIndex(index);
        return consumed![index];
    }

    /// <summary>
    /// Gets where the pointer is, in unclamped virtual coordinates. Zero when no pointer has
    /// reported a position.
    /// </summary>
    /// <remarks>
    /// This is the primary pointer — the last one to report a position. Multi-touch is answered
    /// per-contact through <see cref="InputEvent.PointerId"/> on the events, because a single
    /// "the pointer is here" has no meaning once there are three fingers on the glass.
    /// </remarks>
    public Vector2 PointerPosition => pointerPosition;

    /// <summary>Gets the set of pointer buttons currently held.</summary>
    public PointerButton Buttons => buttons;

    /// <summary>Gets the accumulated scroll of every unconsumed scroll event this tick, in notches.</summary>
    public Vector2 Scroll
    {
        get
        {
            var total = Vector2.Zero;
            for (var index = 0; index < count; index++)
            {
                if (!consumed![index] && events![index].Kind == InputEventKind.Scroll)
                {
                    total += events[index].Scroll;
                }
            }

            return total;
        }
    }

    /// <summary>Gets an allocation-free walk over the unconsumed characters typed this tick.</summary>
    /// <remarks>
    /// The lightweight counterpart to <c>IInteractive.OnTextInput</c>, for a scene that collects
    /// typing without owning a focused node. It yields the same layout-resolved runes the router
    /// would deliver, minus anything a focused node already took.
    /// </remarks>
    public TextInputSequence Text => new(this);

    /// <summary>Returns whether every named button is currently held.</summary>
    /// <param name="button">One or more buttons. <see cref="PointerButton.None"/> returns false.</param>
    public bool IsDown(PointerButton button) =>
        button != PointerButton.None && (buttons & button) == button;

    /// <summary>Returns whether a key is currently held.</summary>
    /// <param name="key">The key to test. <see cref="Key.Unknown"/> returns false.</param>
    /// <remarks>
    /// Host-reserved keys (§9.1) never reach a block, so a scene cannot observe the overlay or
    /// restart key as held any more than it can see their events.
    /// </remarks>
    public bool IsDown(Key key)
    {
        if (key == Key.Unknown || keys is null)
        {
            return false;
        }

        var bit = (int)key;
        var word = bit >> 6;
        return word < keys.Length && (keys[word] & (1UL << (bit & 63))) != 0UL;
    }

    /// <summary>Returns whether a key was freshly pressed this tick and not consumed.</summary>
    /// <param name="key">The key to test.</param>
    /// <remarks>
    /// Auto-repeats do not count: this is an edge, and a key held down produces exactly one
    /// <c>true</c> however long it is held. A scene that wants repeats — a text field's backspace —
    /// reads the events and honours <see cref="InputEvent.IsRepeat"/>, and one that wants the level
    /// asks <see cref="IsDown(Key)"/>.
    /// </remarks>
    public bool WasPressed(Key key)
    {
        if (key == Key.Unknown)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            ref readonly var candidate = ref events![index];
            if (!consumed![index] &&
                candidate.Kind == InputEventKind.KeyDown &&
                candidate.Key == key &&
                !candidate.IsRepeat)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a key was released this tick and not consumed.</summary>
    /// <param name="key">The key to test.</param>
    public bool WasReleased(Key key)
    {
        if (key == Key.Unknown)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            ref readonly var candidate = ref events![index];
            if (!consumed![index] && candidate.Kind == InputEventKind.KeyUp && candidate.Key == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a pointer button went down this tick and was not consumed.</summary>
    /// <param name="button">The single button to test.</param>
    public bool WasPressed(PointerButton button) =>
        HasButtonEdge(InputEventKind.PointerDown, button);

    /// <summary>Returns whether a pointer button came up this tick and was not consumed.</summary>
    /// <param name="button">The single button to test.</param>
    public bool WasReleased(PointerButton button) =>
        HasButtonEdge(InputEventKind.PointerUp, button);

    /// <summary>Marks an event as handled, hiding it from every polling accessor.</summary>
    /// <remarks>
    /// Internal because §9.3 is explicit that polling cannot participate in consumption: consumption
    /// is the router's, earned by a node returning true from an <see cref="IInteractive"/> handler.
    /// </remarks>
    internal void Consume(int index)
    {
        EnsureIndex(index);
        consumed![index] = true;
    }

    private bool AnyKeyDown
    {
        get
        {
            if (keys is null)
            {
                return false;
            }

            for (var index = 0; index < keys.Length; index++)
            {
                if (keys[index] != 0UL)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool HasButtonEdge(InputEventKind kind, PointerButton button)
    {
        if (button == PointerButton.None)
        {
            return false;
        }

        for (var index = 0; index < count; index++)
        {
            ref readonly var candidate = ref events![index];
            if (!consumed![index] && candidate.Kind == kind && candidate.Button == button)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureIndex(int index)
    {
        if ((uint)index >= (uint)count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The input block holds {count} events.");
        }
    }

    /// <summary>Walks the unconsumed characters of one block without allocating.</summary>
    /// <remarks>
    /// A struct enumerator that is its own enumerable, so <c>foreach (var rune in tick.Input.Text)</c>
    /// costs nothing. Like the block it reads, it is valid only during the tick that produced it.
    /// </remarks>
    public struct TextInputSequence
    {
        private readonly InputBlock block;
        private int index;

        internal TextInputSequence(in InputBlock block)
        {
            this.block = block;
            index = -1;
            Current = default;
        }

        /// <summary>Gets the character at the current position.</summary>
        public Rune Current { get; private set; }

        /// <summary>Returns this sequence, so it can be walked with <c>foreach</c>.</summary>
        public TextInputSequence GetEnumerator() => this;

        /// <summary>Advances to the next unconsumed text event, if there is one.</summary>
        public bool MoveNext()
        {
            while (++index < block.count)
            {
                ref readonly var candidate = ref block.events![index];
                if (!block.consumed![index] && candidate.Kind == InputEventKind.Text)
                {
                    Current = candidate.Text;
                    return true;
                }
            }

            return false;
        }
    }
}
