namespace Najm.Core;

/// <summary>Describes a routed key press or release delivered to the focused node.</summary>
/// <remarks>
/// One type for both directions, because a handler almost always wants to see them together — a
/// held key is a press with no release yet, and splitting the callback in two makes that state
/// machine the author's problem. <see cref="IsDown"/> says which this is.
/// </remarks>
public readonly struct KeyArgs
{
    internal KeyArgs(in InputEvent source)
    {
        Key = source.Key;
        Modifiers = source.Modifiers;
        IsRepeat = source.IsRepeat;
        IsDown = source.Kind == InputEventKind.KeyDown;
    }

    /// <summary>Gets the key, identified by physical position (§9.1 — see <see cref="Najm.Core.Key"/>).</summary>
    public Key Key { get; }

    /// <summary>Gets the modifier keys held when the event was produced.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>Gets whether this is a press. False means a release.</summary>
    public bool IsDown { get; }

    /// <summary>Gets whether a press is the platform's auto-repeat rather than a fresh one.</summary>
    public bool IsRepeat { get; }
}
