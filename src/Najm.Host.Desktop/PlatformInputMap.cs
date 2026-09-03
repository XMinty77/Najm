using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Najm.Host.Desktop;

/// <summary>Translates the windowing library's input vocabulary into Najm's.</summary>
/// <remarks>
/// <para>
/// §9.1 makes the host the translator — "hosts translate platform events … into virtual space
/// before the scene ever sees them" — and <see cref="Core.Key"/>'s own remarks say a host
/// "translates into it rather than casting into it". The two enumerations happen to agree on most
/// names and on nothing else: the values differ, the ordering differs, and Silk.NET carries entries
/// Najm has no member for. A cast would compile and produce nonsense.
/// </para>
/// <para>
/// Anything unmapped becomes <see cref="Core.Key.Unknown"/>, which is what
/// <see cref="Core.Key"/> documents it for — a legal value no binding matches. The unmapped set is
/// real rather than theoretical: <c>F13</c>–<c>F25</c> and the two <c>World</c> keys are GLFW
/// values with no Najm member.
/// </para>
/// </remarks>
internal static class PlatformInputMap
{
    /// <summary>Maps one platform key to its Najm equivalent, or <see cref="Core.Key.Unknown"/>.</summary>
    internal static Core.Key ToKey(SilkKey key) => key switch
    {
        SilkKey.Space => Core.Key.Space,
        SilkKey.Apostrophe => Core.Key.Apostrophe,
        SilkKey.Comma => Core.Key.Comma,
        SilkKey.Minus => Core.Key.Minus,
        SilkKey.Period => Core.Key.Period,
        SilkKey.Slash => Core.Key.Slash,
        SilkKey.Number0 => Core.Key.D0,
        SilkKey.Number1 => Core.Key.D1,
        SilkKey.Number2 => Core.Key.D2,
        SilkKey.Number3 => Core.Key.D3,
        SilkKey.Number4 => Core.Key.D4,
        SilkKey.Number5 => Core.Key.D5,
        SilkKey.Number6 => Core.Key.D6,
        SilkKey.Number7 => Core.Key.D7,
        SilkKey.Number8 => Core.Key.D8,
        SilkKey.Number9 => Core.Key.D9,
        SilkKey.Semicolon => Core.Key.Semicolon,
        SilkKey.Equal => Core.Key.Equal,
        SilkKey.A => Core.Key.A,
        SilkKey.B => Core.Key.B,
        SilkKey.C => Core.Key.C,
        SilkKey.D => Core.Key.D,
        SilkKey.E => Core.Key.E,
        SilkKey.F => Core.Key.F,
        SilkKey.G => Core.Key.G,
        SilkKey.H => Core.Key.H,
        SilkKey.I => Core.Key.I,
        SilkKey.J => Core.Key.J,
        SilkKey.K => Core.Key.K,
        SilkKey.L => Core.Key.L,
        SilkKey.M => Core.Key.M,
        SilkKey.N => Core.Key.N,
        SilkKey.O => Core.Key.O,
        SilkKey.P => Core.Key.P,
        SilkKey.Q => Core.Key.Q,
        SilkKey.R => Core.Key.R,
        SilkKey.S => Core.Key.S,
        SilkKey.T => Core.Key.T,
        SilkKey.U => Core.Key.U,
        SilkKey.V => Core.Key.V,
        SilkKey.W => Core.Key.W,
        SilkKey.X => Core.Key.X,
        SilkKey.Y => Core.Key.Y,
        SilkKey.Z => Core.Key.Z,
        SilkKey.LeftBracket => Core.Key.LeftBracket,
        SilkKey.BackSlash => Core.Key.Backslash,
        SilkKey.RightBracket => Core.Key.RightBracket,
        SilkKey.GraveAccent => Core.Key.GraveAccent,
        SilkKey.Escape => Core.Key.Escape,
        SilkKey.Enter => Core.Key.Enter,
        SilkKey.Tab => Core.Key.Tab,
        SilkKey.Backspace => Core.Key.Backspace,
        SilkKey.Insert => Core.Key.Insert,
        SilkKey.Delete => Core.Key.Delete,
        SilkKey.Right => Core.Key.Right,
        SilkKey.Left => Core.Key.Left,
        SilkKey.Down => Core.Key.Down,
        SilkKey.Up => Core.Key.Up,
        SilkKey.PageUp => Core.Key.PageUp,
        SilkKey.PageDown => Core.Key.PageDown,
        SilkKey.Home => Core.Key.Home,
        SilkKey.End => Core.Key.End,
        SilkKey.CapsLock => Core.Key.CapsLock,
        SilkKey.ScrollLock => Core.Key.ScrollLock,
        SilkKey.NumLock => Core.Key.NumLock,
        SilkKey.PrintScreen => Core.Key.PrintScreen,
        SilkKey.Pause => Core.Key.Pause,
        SilkKey.F1 => Core.Key.F1,
        SilkKey.F2 => Core.Key.F2,
        SilkKey.F3 => Core.Key.F3,
        SilkKey.F4 => Core.Key.F4,
        SilkKey.F5 => Core.Key.F5,
        SilkKey.F6 => Core.Key.F6,
        SilkKey.F7 => Core.Key.F7,
        SilkKey.F8 => Core.Key.F8,
        SilkKey.F9 => Core.Key.F9,
        SilkKey.F10 => Core.Key.F10,
        SilkKey.F11 => Core.Key.F11,
        SilkKey.F12 => Core.Key.F12,
        SilkKey.Keypad0 => Core.Key.Numpad0,
        SilkKey.Keypad1 => Core.Key.Numpad1,
        SilkKey.Keypad2 => Core.Key.Numpad2,
        SilkKey.Keypad3 => Core.Key.Numpad3,
        SilkKey.Keypad4 => Core.Key.Numpad4,
        SilkKey.Keypad5 => Core.Key.Numpad5,
        SilkKey.Keypad6 => Core.Key.Numpad6,
        SilkKey.Keypad7 => Core.Key.Numpad7,
        SilkKey.Keypad8 => Core.Key.Numpad8,
        SilkKey.Keypad9 => Core.Key.Numpad9,
        SilkKey.KeypadDecimal => Core.Key.NumpadDecimal,
        SilkKey.KeypadDivide => Core.Key.NumpadDivide,
        SilkKey.KeypadMultiply => Core.Key.NumpadMultiply,
        SilkKey.KeypadSubtract => Core.Key.NumpadSubtract,
        SilkKey.KeypadAdd => Core.Key.NumpadAdd,
        SilkKey.KeypadEnter => Core.Key.NumpadEnter,
        SilkKey.KeypadEqual => Core.Key.NumpadEqual,
        SilkKey.ShiftLeft => Core.Key.LeftShift,
        SilkKey.ControlLeft => Core.Key.LeftControl,
        SilkKey.AltLeft => Core.Key.LeftAlt,
        SilkKey.SuperLeft => Core.Key.LeftSuper,
        SilkKey.ShiftRight => Core.Key.RightShift,
        SilkKey.ControlRight => Core.Key.RightControl,
        SilkKey.AltRight => Core.Key.RightAlt,
        SilkKey.SuperRight => Core.Key.RightSuper,
        SilkKey.Menu => Core.Key.Menu,
        _ => Core.Key.Unknown,
    };

    /// <summary>Maps one platform mouse button to its Najm equivalent, or <c>None</c>.</summary>
    /// <remarks>
    /// <c>None</c> means "do not deliver": <see cref="Core.InputBuffer"/> requires a press or
    /// release to name exactly one defined button, and Silk.NET's <c>Button6</c> and beyond have no
    /// Najm member to name. Najm stops at <c>X2</c> because that is where mice stop being ordinary.
    /// </remarks>
    internal static Core.PointerButton ToButton(SilkMouseButton button) => button switch
    {
        SilkMouseButton.Left => Core.PointerButton.Left,
        SilkMouseButton.Right => Core.PointerButton.Right,
        SilkMouseButton.Middle => Core.PointerButton.Middle,
        SilkMouseButton.Button4 => Core.PointerButton.X1,
        SilkMouseButton.Button5 => Core.PointerButton.X2,
        _ => Core.PointerButton.None,
    };
}
