using System.Numerics;
using System.Text;

namespace Najm.Core;

/// <summary>Owns the pooled per-frame buffers behind an <see cref="InputBlock"/> and the level state that outlives one tick.</summary>
/// <remarks>
/// <para>
/// <strong>This is the host's half of §9.1.</strong> A host pumps its platform's event queue, maps
/// each event into Najm's vocabulary, inverse-letterboxes pointer coordinates into virtual space,
/// and pushes the result here; then it hands <see cref="Block"/> to the tick. One buffer lives for
/// the run: <see cref="BeginFrame"/> empties the event list in place and the arrays are refilled,
/// never reallocated, so a warm frame allocates nothing (§3.6). Growth happens only when a frame
/// carries more events than any frame before it, which is a transition cost §3.6 permits and
/// measures.
/// </para>
/// <para>
/// <strong>Coordinates arrive already in virtual space.</strong> §9.1 puts the letterbox inversion
/// on the host, before the scene sees anything, and requires the result to be delivered
/// <em>unclamped</em> — a pointer outside the content rect maps linearly to a negative or
/// out-of-range virtual coordinate, which is what keeps an off-canvas drag smooth. Nothing here
/// clamps, and nothing here scales.
/// </para>
/// <para>
/// <strong>Host-reserved keys never reach a scene.</strong> §9.1 reserves the overlay toggle
/// (conventionally <see cref="Key.F1"/>) and the warm-restart key (conventionally
/// <see cref="Key.F5"/>, §15) to the host. A host declares those through <see cref="Reserve"/> —
/// this type ships with none reserved, because Core has neither an overlay nor a restart to bind
/// and inventing the defaults here would put a host policy inside the engine. A reserved key's
/// presses and releases are dropped and its held state is cleared, so a scene cannot observe it as
/// an event or as a snapshot. <see cref="PressKey"/> returns false when it drops one, which is the
/// host's signal to act on it itself.
/// </para>
/// <para>
/// <strong>Not thread-safe, by the same rule as everything else.</strong> §3.5 makes the engine
/// single-threaded; a host that pumps events on another thread marshals them to the tick thread
/// before pushing them here.
/// </para>
/// </remarks>
public sealed class InputBuffer
{
    /// <summary>The number of 64-bit words needed to hold one bit per <see cref="Key"/>.</summary>
    /// <remarks><see cref="Key.Menu"/> is the highest value; a test pins that.</remarks>
    private const int KeyWords = ((int)Key.Menu / 64) + 1;

    private const int MinimumCapacity = 8;

    private readonly ulong[] keys = new ulong[KeyWords];
    private readonly ulong[] reserved = new ulong[KeyWords];
    private InputEvent[] events;
    private bool[] consumed;
    private Vector2 pointerPosition;
    private PointerButton buttons;
    private int count;

    /// <summary>Creates a buffer sized for a typical frame's traffic.</summary>
    /// <param name="capacity">
    /// The number of events the buffer starts able to hold without growing. It grows on demand, so
    /// this is a warm-up hint and not a limit.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public InputBuffer(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        var initial = Math.Max(capacity, MinimumCapacity);
        events = new InputEvent[initial];
        consumed = new bool[initial];
    }

    /// <summary>Gets the number of events pushed since the last <see cref="BeginFrame"/>.</summary>
    public int EventCount => count;

    /// <summary>Gets how many events this buffer can hold before it has to grow.</summary>
    public int Capacity => events.Length;

    /// <summary>
    /// Gets the block over this buffer's current contents, for handing to
    /// <see cref="TickContext(in TimeInfo, in InputBlock)"/>.
    /// </summary>
    /// <remarks>
    /// The block is a view: it stays valid exactly until the next <see cref="BeginFrame"/>, which is
    /// the point of the pooling. Nothing here copies, so reading this property in a loop is free.
    /// </remarks>
    public InputBlock Block => new(events, consumed, keys, count, pointerPosition, buttons);

    /// <summary>Empties the event list for a new frame, keeping held keys, held buttons, and the pointer position.</summary>
    /// <remarks>
    /// Events are per-frame and snapshots are not: a key held across ten frames is down in all ten,
    /// and produced a single press event in the first. The host calls this once, before it drains
    /// its platform queue.
    /// </remarks>
    public void BeginFrame() => count = 0;

    /// <summary>Clears every held key, every held button, and the pointer position.</summary>
    /// <remarks>
    /// For the moment a host loses focus. Without it, a window that loses focus with a key down
    /// leaves the scene believing that key is held forever, because the release goes to whoever took
    /// focus. Pending events are not discarded — what already happened, happened.
    /// </remarks>
    public void ResetState()
    {
        Array.Clear(keys);
        buttons = PointerButton.None;
        pointerPosition = Vector2.Zero;
    }

    /// <summary>Records that a pointer moved to a virtual-space position.</summary>
    /// <param name="pointerId">The pointer's identity; mice use a single stable value.</param>
    /// <param name="virtualPosition">The finite, unclamped virtual-space position (§3.3).</param>
    /// <param name="modifiers">The modifier keys held at the moment of the move.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    public void MovePointer(int pointerId, Vector2 virtualPosition, KeyModifiers modifiers = KeyModifiers.None)
    {
        EnsureFinite(virtualPosition, nameof(virtualPosition));
        pointerPosition = virtualPosition;
        Append(InputEvent.PointerMove(pointerId, virtualPosition, buttons, modifiers));
    }

    /// <summary>Records that a pointer button went down.</summary>
    /// <param name="pointerId">The pointer's identity.</param>
    /// <param name="virtualPosition">The finite, unclamped virtual-space position (§3.3).</param>
    /// <param name="button">The single button that went down.</param>
    /// <param name="modifiers">The modifier keys held at the moment of the press.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position is not finite, or the button is not a single defined button.
    /// </exception>
    public void PressPointer(
        int pointerId,
        Vector2 virtualPosition,
        PointerButton button,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        EnsureFinite(virtualPosition, nameof(virtualPosition));
        EnsureSingleButton(button);

        pointerPosition = virtualPosition;
        buttons |= button;
        Append(InputEvent.PointerDown(pointerId, virtualPosition, button, buttons, modifiers));
    }

    /// <summary>Records that a pointer button came up.</summary>
    /// <param name="pointerId">The pointer's identity.</param>
    /// <param name="virtualPosition">The finite, unclamped virtual-space position (§3.3).</param>
    /// <param name="button">The single button that came up.</param>
    /// <param name="modifiers">The modifier keys held at the moment of the release.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position is not finite, or the button is not a single defined button.
    /// </exception>
    public void ReleasePointer(
        int pointerId,
        Vector2 virtualPosition,
        PointerButton button,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        EnsureFinite(virtualPosition, nameof(virtualPosition));
        EnsureSingleButton(button);

        pointerPosition = virtualPosition;
        buttons &= ~button;
        Append(InputEvent.PointerUp(pointerId, virtualPosition, button, buttons, modifiers));
    }

    /// <summary>Records a wheel or trackpad scroll at a pointer's position.</summary>
    /// <param name="pointerId">The pointer's identity.</param>
    /// <param name="virtualPosition">The finite, unclamped virtual-space position (§3.3).</param>
    /// <param name="delta">The finite scroll in notches; positive Y scrolls away from the user.</param>
    /// <param name="modifiers">The modifier keys held at the moment of the scroll.</param>
    /// <exception cref="ArgumentOutOfRangeException">The position or the delta is not finite.</exception>
    public void ScrollPointer(
        int pointerId,
        Vector2 virtualPosition,
        Vector2 delta,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        EnsureFinite(virtualPosition, nameof(virtualPosition));
        EnsureFinite(delta, nameof(delta));

        pointerPosition = virtualPosition;
        Append(InputEvent.Scrolled(pointerId, virtualPosition, delta, buttons, modifiers));
    }

    /// <summary>Records that a key went down, unless the host has reserved it.</summary>
    /// <param name="key">The key, by physical position (§9.1 — see <see cref="Najm.Core.Key"/>).</param>
    /// <param name="modifiers">The modifier keys held at the moment of the press.</param>
    /// <param name="isRepeat">Whether this is the platform's auto-repeat rather than a fresh press.</param>
    /// <returns>
    /// False when the key is reserved and nothing was recorded — the host's cue to handle it itself.
    /// </returns>
    /// <exception cref="ArgumentException">The key is not a defined value.</exception>
    public bool PressKey(Key key, KeyModifiers modifiers = KeyModifiers.None, bool isRepeat = false)
    {
        EnsureDefinedKey(key);
        if (IsReserved(key))
        {
            return false;
        }

        SetKeyBit(keys, key, down: true);
        Append(InputEvent.KeyDown(key, modifiers, isRepeat));
        return true;
    }

    /// <summary>Records that a key came up, unless the host has reserved it.</summary>
    /// <param name="key">The key, by physical position.</param>
    /// <param name="modifiers">The modifier keys held at the moment of the release.</param>
    /// <returns>False when the key is reserved and nothing was recorded.</returns>
    /// <exception cref="ArgumentException">The key is not a defined value.</exception>
    public bool ReleaseKey(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        EnsureDefinedKey(key);
        if (IsReserved(key))
        {
            return false;
        }

        SetKeyBit(keys, key, down: false);
        Append(InputEvent.KeyUp(key, modifiers));
        return true;
    }

    /// <summary>Records a character the platform's layout produced.</summary>
    /// <param name="text">The character, already resolved through layout, dead keys, and modifiers.</param>
    /// <param name="modifiers">The modifier keys held at the moment it was produced.</param>
    /// <remarks>
    /// Text is not filtered by <see cref="Reserve"/>: a text event carries no key position, so this
    /// buffer cannot know which key produced it. A host reserving a key that types something
    /// suppresses that key's text event on its own side — which the conventional reservations,
    /// <see cref="Key.F1"/> and <see cref="Key.F5"/>, never produce.
    /// </remarks>
    public void EnterText(Rune text, KeyModifiers modifiers = KeyModifiers.None) =>
        Append(InputEvent.TextEntered(text, modifiers));

    /// <summary>Reserves a key to the host, so no scene ever sees it (§9.1).</summary>
    /// <param name="key">The key to reserve. Reserving one already held clears its held state.</param>
    /// <exception cref="ArgumentException">The key is not a defined value.</exception>
    public void Reserve(Key key)
    {
        EnsureDefinedKey(key);
        SetKeyBit(reserved, key, down: true);

        // A key reserved while it is held would otherwise stay held forever: its release is dropped
        // by the reservation that now exists.
        SetKeyBit(keys, key, down: false);
    }

    /// <summary>Stops reserving a key, so it reaches scenes again.</summary>
    /// <param name="key">The key to release back to the scene.</param>
    /// <exception cref="ArgumentException">The key is not a defined value.</exception>
    public void Unreserve(Key key)
    {
        EnsureDefinedKey(key);
        SetKeyBit(reserved, key, down: false);
    }

    /// <summary>Returns whether a key is reserved to the host.</summary>
    /// <param name="key">The key to test.</param>
    public bool IsReserved(Key key) => TestKeyBit(reserved, key);

    private void Append(in InputEvent value)
    {
        if (count == events.Length)
        {
            Grow();
        }

        events[count] = value;
        consumed[count] = false;
        count++;
    }

    private void Grow()
    {
        var grown = events.Length * 2;
        Array.Resize(ref events, grown);
        Array.Resize(ref consumed, grown);
    }

    private static void SetKeyBit(ulong[] words, Key key, bool down)
    {
        if (key == Key.Unknown)
        {
            return;
        }

        var bit = (int)key;
        var mask = 1UL << (bit & 63);
        if (down)
        {
            words[bit >> 6] |= mask;
        }
        else
        {
            words[bit >> 6] &= ~mask;
        }
    }

    private static bool TestKeyBit(ulong[] words, Key key)
    {
        if (key == Key.Unknown)
        {
            return false;
        }

        var bit = (int)key;
        return (words[bit >> 6] & (1UL << (bit & 63))) != 0UL;
    }

    private static void EnsureDefinedKey(Key key)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentException("The key is not a defined value.", nameof(key));
        }
    }

    private static void EnsureSingleButton(PointerButton button)
    {
        if (button is not (PointerButton.Left
            or PointerButton.Middle
            or PointerButton.Right
            or PointerButton.X1
            or PointerButton.X2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "A press or release names exactly one button; PointerButton is a flags set only in snapshots.");
        }
    }

    private static void EnsureFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Input coordinates must be finite. They are deliberately unclamped, but never NaN.");
        }
    }
}
