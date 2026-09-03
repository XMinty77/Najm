using System.Text;

namespace Najm.Core;

/// <summary>Describes one character typed, delivered to the focused node.</summary>
/// <remarks>
/// This is the layout-resolved result of typing rather than a key position, which is the whole
/// reason §9.1 calls it out separately: a <c>TextBox</c> fed from <see cref="Key"/> codes types
/// gibberish on any layout but the one it was written on.
/// </remarks>
public readonly struct TextInputArgs
{
    internal TextInputArgs(in InputEvent source)
    {
        Text = source.Text;
        Modifiers = source.Modifiers;
    }

    /// <summary>Gets the character, as one <see cref="System.Text.Rune"/> rather than a surrogate pair.</summary>
    public Rune Text { get; }

    /// <summary>Gets the modifier keys held when the character was produced.</summary>
    public KeyModifiers Modifiers { get; }
}
