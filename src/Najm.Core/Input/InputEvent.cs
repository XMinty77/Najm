using System.Numerics;
using System.Text;

namespace Najm.Core;

/// <summary>Describes one thing that happened to the pointer, the keyboard, or the wheel.</summary>
/// <remarks>
/// <para>
/// <strong>One struct for every kind, on purpose.</strong> ARCHITECTURE §9.1 says events are
/// consumed in order and §9.2 routes them in order, so a pointer press that lands between two
/// keystrokes has to stay between them. A per-kind hierarchy would need either a common base — which
/// means a heap object per event, against §3.6 — or parallel queues, which lose the interleaving.
/// A single value type keeps one ordered buffer, and the fields a kind does not use read as their
/// defaults rather than as garbage.
/// </para>
/// <para>
/// <strong>Coordinates are virtual (§3.3), and they are not clamped.</strong> The host has already
/// inverse-letterboxed them, so <see cref="Position"/> is directly comparable to the coordinates a
/// <c>ScreenLayer</c> node is written in. §9.1 is explicit that positions outside the letterbox map
/// linearly and arrive unclamped: they may be negative or exceed the scene's virtual resolution,
/// which is what keeps a drag that leaves the window smooth instead of pinning to the edge. A scene
/// that wants containment tests bounds itself.
/// </para>
/// </remarks>
public readonly struct InputEvent
{
    private readonly int codePoint;

    private InputEvent(
        InputEventKind kind,
        int pointerId,
        Vector2 position,
        Vector2 scroll,
        PointerButton button,
        PointerButton buttons,
        Key key,
        KeyModifiers modifiers,
        bool isRepeat,
        int codePoint)
    {
        Kind = kind;
        PointerId = pointerId;
        Position = position;
        Scroll = scroll;
        Button = button;
        Buttons = buttons;
        Key = key;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
        this.codePoint = codePoint;
    }

    /// <summary>Gets what happened.</summary>
    public InputEventKind Kind { get; }

    /// <summary>
    /// Gets the pointer this event belongs to. Mice report a single stable id; each touch or pen
    /// contact gets its own for as long as it lasts. Zero on keyboard and text events.
    /// </summary>
    public int PointerId { get; }

    /// <summary>Gets the unclamped virtual-space pointer position. Zero on keyboard and text events.</summary>
    public Vector2 Position { get; }

    /// <summary>
    /// Gets the scroll delta of a <see cref="InputEventKind.Scroll"/> event, in notches: positive Y
    /// is a scroll away from the user and positive X is a scroll to the right. Zero on every other
    /// kind.
    /// </summary>
    /// <remarks>
    /// Notches rather than pixels, because "how far did the wheel turn" is the only question every
    /// platform answers the same way. A trackpad's fine-grained scroll arrives as a fractional
    /// notch.
    /// </remarks>
    public Vector2 Scroll { get; }

    /// <summary>
    /// Gets the single button that went down or came up, or <see cref="PointerButton.None"/> on
    /// every other kind.
    /// </summary>
    public PointerButton Button { get; }

    /// <summary>
    /// Gets the buttons held while this pointer event happened, including the one
    /// <see cref="Button"/> names on a press and excluding it on a release.
    /// </summary>
    public PointerButton Buttons { get; }

    /// <summary>Gets the key of a key event, or <see cref="Najm.Core.Key.Unknown"/> otherwise.</summary>
    public Key Key { get; }

    /// <summary>Gets the modifiers held when this event was produced.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Gets whether a <see cref="InputEventKind.KeyDown"/> is the platform's auto-repeat rather than
    /// a fresh press. False on every other kind.
    /// </summary>
    /// <remarks>
    /// A text field wants repeats — holding backspace should keep deleting. A game-style control
    /// binding does not. Both readings are available because the fact travels rather than being
    /// filtered out by the host.
    /// </remarks>
    public bool IsRepeat { get; }

    /// <summary>
    /// Gets the character a <see cref="InputEventKind.Text"/> event produced, or
    /// <c>default</c> otherwise.
    /// </summary>
    /// <remarks>
    /// A <see cref="System.Text.Rune"/> rather than a <c>char</c>, so an astral-plane character is
    /// one event and not a surrogate pair split across two.
    /// </remarks>
    public Rune Text => codePoint == 0 ? default : new Rune(codePoint);

    /// <summary>Gets whether this event carries pointer coordinates — a move, press, release, or scroll.</summary>
    public bool IsPointerEvent =>
        Kind is InputEventKind.PointerMove
            or InputEventKind.PointerDown
            or InputEventKind.PointerUp
            or InputEventKind.Scroll;

    /// <summary>Gets whether this event is a key press, key release, or text entry.</summary>
    public bool IsKeyboardEvent =>
        Kind is InputEventKind.KeyDown or InputEventKind.KeyUp or InputEventKind.Text;

    internal static InputEvent PointerMove(
        int pointerId,
        Vector2 position,
        PointerButton buttons,
        KeyModifiers modifiers) =>
        new(
            InputEventKind.PointerMove,
            pointerId,
            position,
            Vector2.Zero,
            PointerButton.None,
            buttons,
            Key.Unknown,
            modifiers,
            isRepeat: false,
            codePoint: 0);

    internal static InputEvent PointerDown(
        int pointerId,
        Vector2 position,
        PointerButton button,
        PointerButton buttons,
        KeyModifiers modifiers) =>
        new(
            InputEventKind.PointerDown,
            pointerId,
            position,
            Vector2.Zero,
            button,
            buttons,
            Key.Unknown,
            modifiers,
            isRepeat: false,
            codePoint: 0);

    internal static InputEvent PointerUp(
        int pointerId,
        Vector2 position,
        PointerButton button,
        PointerButton buttons,
        KeyModifiers modifiers) =>
        new(
            InputEventKind.PointerUp,
            pointerId,
            position,
            Vector2.Zero,
            button,
            buttons,
            Key.Unknown,
            modifiers,
            isRepeat: false,
            codePoint: 0);

    internal static InputEvent Scrolled(
        int pointerId,
        Vector2 position,
        Vector2 scroll,
        PointerButton buttons,
        KeyModifiers modifiers) =>
        new(
            InputEventKind.Scroll,
            pointerId,
            position,
            scroll,
            PointerButton.None,
            buttons,
            Key.Unknown,
            modifiers,
            isRepeat: false,
            codePoint: 0);

    internal static InputEvent KeyDown(Key key, KeyModifiers modifiers, bool isRepeat) =>
        new(
            InputEventKind.KeyDown,
            pointerId: 0,
            Vector2.Zero,
            Vector2.Zero,
            PointerButton.None,
            PointerButton.None,
            key,
            modifiers,
            isRepeat,
            codePoint: 0);

    internal static InputEvent KeyUp(Key key, KeyModifiers modifiers) =>
        new(
            InputEventKind.KeyUp,
            pointerId: 0,
            Vector2.Zero,
            Vector2.Zero,
            PointerButton.None,
            PointerButton.None,
            key,
            modifiers,
            isRepeat: false,
            codePoint: 0);

    internal static InputEvent TextEntered(Rune text, KeyModifiers modifiers) =>
        new(
            InputEventKind.Text,
            pointerId: 0,
            Vector2.Zero,
            Vector2.Zero,
            PointerButton.None,
            PointerButton.None,
            Key.Unknown,
            modifiers,
            isRepeat: false,
            text.Value);
}
