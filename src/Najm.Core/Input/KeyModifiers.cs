namespace Najm.Core;

/// <summary>Describes the modifier keys held when an input event was produced.</summary>
/// <remarks>
/// <para>
/// Modifiers travel on the event rather than being polled, because the answer that matters is the
/// one that was true when the key or button was pressed, not the one that is true when a handler
/// gets around to reading it.
/// </para>
/// <para>
/// <see cref="Super"/> is the platform key — Windows, Command, or Meta. Najm names it once, in
/// neutral terms, so a scene that ships on three platforms does not branch on which one it is.
/// Left and right instances of a modifier are not distinguished here; the individual
/// <see cref="Key.LeftShift"/>-style codes remain available as ordinary keys for the rare scene
/// that cares.
/// </para>
/// </remarks>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier is held.</summary>
    None = 0,

    /// <summary>Either shift key is held.</summary>
    Shift = 1,

    /// <summary>Either control key is held.</summary>
    Control = 2,

    /// <summary>Either alt (option) key is held.</summary>
    Alt = 4,

    /// <summary>Either platform key — Windows, Command, or Meta — is held.</summary>
    Super = 8,
}
