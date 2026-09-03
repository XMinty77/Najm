namespace Najm.Core;

/// <summary>Identifies a pointer button, and — as a set of flags — the buttons currently held.</summary>
/// <remarks>
/// <para>
/// ARCHITECTURE §9.3 gives <see cref="PointerArgs"/> a singular <c>Button</c>, and §9.1 gives the
/// polling snapshot a plural "pointer position/buttons". Those are the same vocabulary seen once per
/// event and once per frame, so this is one flags enum rather than two types: an event carries
/// exactly one flag — the button that went down or came up — and
/// <see cref="InputBlock.Buttons"/> carries the union of everything held. A pointer that moves
/// while nothing is pressed reports <see cref="None"/> on both.
/// </para>
/// <para>
/// The set is closed at the five buttons a mouse actually reports. Touch and pen contacts are
/// pointers in their own right, distinguished by <see cref="InputEvent.PointerId"/>, and press as
/// <see cref="Left"/> — which is what makes a scene written for a mouse work under a finger without
/// a second code path.
/// </para>
/// </remarks>
[Flags]
public enum PointerButton
{
    /// <summary>No button. This is what a plain move or a wheel event carries.</summary>
    None = 0,

    /// <summary>The primary button, and the one a touch or pen contact presses.</summary>
    Left = 1,

    /// <summary>The middle button, conventionally the wheel.</summary>
    Middle = 2,

    /// <summary>The secondary button.</summary>
    Right = 4,

    /// <summary>The first extended button, conventionally "back".</summary>
    X1 = 8,

    /// <summary>The second extended button, conventionally "forward".</summary>
    X2 = 16,
}
