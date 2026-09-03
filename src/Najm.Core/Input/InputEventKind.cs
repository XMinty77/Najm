namespace Najm.Core;

/// <summary>Names what an <see cref="InputEvent"/> is.</summary>
/// <remarks>
/// The five kinds ARCHITECTURE §9.1 lists — pointer, keyboard down/up, text, scroll — expand to
/// seven values here because pointer presses, releases, and moves are distinct dispatches even
/// though they share a shape. Everything in this set is a discrete thing that happened at a moment;
/// continuous state (where the pointer is, which keys are held) lives in the block's snapshots
/// instead.
/// </remarks>
public enum InputEventKind
{
    /// <summary>The zero value, carried only by the default <see cref="InputEvent"/>.</summary>
    None = 0,

    /// <summary>A pointer moved. Carries the new position and the buttons held during the move.</summary>
    PointerMove,

    /// <summary>A pointer button went down.</summary>
    PointerDown,

    /// <summary>A pointer button came up.</summary>
    PointerUp,

    /// <summary>A wheel or trackpad scroll, positioned at the pointer that produced it.</summary>
    Scroll,

    /// <summary>A key went down, possibly as an auto-repeat.</summary>
    KeyDown,

    /// <summary>A key came up.</summary>
    KeyUp,

    /// <summary>
    /// A character was produced. This is the layout-resolved result of typing, not a key position —
    /// see <see cref="Key"/> for why the two are separate events.
    /// </summary>
    Text,
}
