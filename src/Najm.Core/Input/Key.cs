namespace Najm.Core;

/// <summary>Identifies a physical key by its unshifted US-layout meaning.</summary>
/// <remarks>
/// <para>
/// <strong>This is a position, not a character.</strong> <see cref="Key.Q"/> is the key where Q sits
/// on a US keyboard; on an AZERTY layout the same physical key still reports
/// <see cref="Key.Q"/> even though pressing it types "a". That is the right primitive for controls —
/// WASD stays a square under every layout — and exactly the wrong one for entering text. Text is
/// what <see cref="InputEventKind.Text"/> is for: it carries the <see cref="System.Text.Rune"/> the
/// platform decided the keystroke produced, after layout, dead keys, and modifiers.
/// ARCHITECTURE §9.1 names that trap in one line — "key codes alone are the classic trap" — and
/// this pair of event kinds is the answer to it.
/// </para>
/// <para>
/// The set is deliberately the ordinary hundred-odd keys of a physical keyboard. It is a plain
/// enumeration and not a scancode: values are Najm's own and carry no platform numbering, so a host
/// translates into it rather than casting into it. Anything a host cannot map lands on
/// <see cref="Unknown"/>, which is a legal value that no binding should match.
/// </para>
/// <para>
/// IME composition and complex text are deferred (§9.4); a host that runs one delivers its committed
/// result as ordinary <see cref="InputEventKind.Text"/> events.
/// </para>
/// </remarks>
public enum Key
{
    /// <summary>A key the host could not translate. Never equal to a real key.</summary>
    Unknown = 0,

    /// <summary>The space bar.</summary>
    Space,

    /// <summary>The apostrophe/quote key.</summary>
    Apostrophe,

    /// <summary>The comma key.</summary>
    Comma,

    /// <summary>The hyphen/underscore key.</summary>
    Minus,

    /// <summary>The period key.</summary>
    Period,

    /// <summary>The forward-slash key.</summary>
    Slash,

    /// <summary>The 0 key on the number row.</summary>
    D0,

    /// <summary>The 1 key on the number row.</summary>
    D1,

    /// <summary>The 2 key on the number row.</summary>
    D2,

    /// <summary>The 3 key on the number row.</summary>
    D3,

    /// <summary>The 4 key on the number row.</summary>
    D4,

    /// <summary>The 5 key on the number row.</summary>
    D5,

    /// <summary>The 6 key on the number row.</summary>
    D6,

    /// <summary>The 7 key on the number row.</summary>
    D7,

    /// <summary>The 8 key on the number row.</summary>
    D8,

    /// <summary>The 9 key on the number row.</summary>
    D9,

    /// <summary>The semicolon key.</summary>
    Semicolon,

    /// <summary>The equals/plus key.</summary>
    Equal,

    /// <summary>The A key.</summary>
    A,

    /// <summary>The B key.</summary>
    B,

    /// <summary>The C key.</summary>
    C,

    /// <summary>The D key.</summary>
    D,

    /// <summary>The E key.</summary>
    E,

    /// <summary>The F key.</summary>
    F,

    /// <summary>The G key.</summary>
    G,

    /// <summary>The H key.</summary>
    H,

    /// <summary>The I key.</summary>
    I,

    /// <summary>The J key.</summary>
    J,

    /// <summary>The K key.</summary>
    K,

    /// <summary>The L key.</summary>
    L,

    /// <summary>The M key.</summary>
    M,

    /// <summary>The N key.</summary>
    N,

    /// <summary>The O key.</summary>
    O,

    /// <summary>The P key.</summary>
    P,

    /// <summary>The Q key.</summary>
    Q,

    /// <summary>The R key.</summary>
    R,

    /// <summary>The S key.</summary>
    S,

    /// <summary>The T key.</summary>
    T,

    /// <summary>The U key.</summary>
    U,

    /// <summary>The V key.</summary>
    V,

    /// <summary>The W key.</summary>
    W,

    /// <summary>The X key.</summary>
    X,

    /// <summary>The Y key.</summary>
    Y,

    /// <summary>The Z key.</summary>
    Z,

    /// <summary>The left square-bracket key.</summary>
    LeftBracket,

    /// <summary>The backslash key.</summary>
    Backslash,

    /// <summary>The right square-bracket key.</summary>
    RightBracket,

    /// <summary>The backtick/tilde key.</summary>
    GraveAccent,

    /// <summary>The escape key.</summary>
    Escape,

    /// <summary>The return/enter key on the main block.</summary>
    Enter,

    /// <summary>The tab key.</summary>
    Tab,

    /// <summary>The backspace key.</summary>
    Backspace,

    /// <summary>The insert key.</summary>
    Insert,

    /// <summary>The forward-delete key.</summary>
    Delete,

    /// <summary>The right arrow key.</summary>
    Right,

    /// <summary>The left arrow key.</summary>
    Left,

    /// <summary>The down arrow key.</summary>
    Down,

    /// <summary>The up arrow key.</summary>
    Up,

    /// <summary>The page-up key.</summary>
    PageUp,

    /// <summary>The page-down key.</summary>
    PageDown,

    /// <summary>The home key.</summary>
    Home,

    /// <summary>The end key.</summary>
    End,

    /// <summary>The caps-lock key.</summary>
    CapsLock,

    /// <summary>The scroll-lock key.</summary>
    ScrollLock,

    /// <summary>The num-lock key.</summary>
    NumLock,

    /// <summary>The print-screen key.</summary>
    PrintScreen,

    /// <summary>The pause/break key.</summary>
    Pause,

    /// <summary>The F1 key. Reserved by desktop hosts for the debug overlay by default (§9.1, §15).</summary>
    F1,

    /// <summary>The F2 key.</summary>
    F2,

    /// <summary>The F3 key.</summary>
    F3,

    /// <summary>The F4 key.</summary>
    F4,

    /// <summary>The F5 key. Reserved by desktop hosts for warm restart by default (§9.1, §15).</summary>
    F5,

    /// <summary>The F6 key.</summary>
    F6,

    /// <summary>The F7 key.</summary>
    F7,

    /// <summary>The F8 key.</summary>
    F8,

    /// <summary>The F9 key.</summary>
    F9,

    /// <summary>The F10 key.</summary>
    F10,

    /// <summary>The F11 key.</summary>
    F11,

    /// <summary>The F12 key.</summary>
    F12,

    /// <summary>The 0 key on the numeric keypad.</summary>
    Numpad0,

    /// <summary>The 1 key on the numeric keypad.</summary>
    Numpad1,

    /// <summary>The 2 key on the numeric keypad.</summary>
    Numpad2,

    /// <summary>The 3 key on the numeric keypad.</summary>
    Numpad3,

    /// <summary>The 4 key on the numeric keypad.</summary>
    Numpad4,

    /// <summary>The 5 key on the numeric keypad.</summary>
    Numpad5,

    /// <summary>The 6 key on the numeric keypad.</summary>
    Numpad6,

    /// <summary>The 7 key on the numeric keypad.</summary>
    Numpad7,

    /// <summary>The 8 key on the numeric keypad.</summary>
    Numpad8,

    /// <summary>The 9 key on the numeric keypad.</summary>
    Numpad9,

    /// <summary>The decimal-point key on the numeric keypad.</summary>
    NumpadDecimal,

    /// <summary>The divide key on the numeric keypad.</summary>
    NumpadDivide,

    /// <summary>The multiply key on the numeric keypad.</summary>
    NumpadMultiply,

    /// <summary>The subtract key on the numeric keypad.</summary>
    NumpadSubtract,

    /// <summary>The add key on the numeric keypad.</summary>
    NumpadAdd,

    /// <summary>The enter key on the numeric keypad.</summary>
    NumpadEnter,

    /// <summary>The equals key on the numeric keypad.</summary>
    NumpadEqual,

    /// <summary>The left shift key.</summary>
    LeftShift,

    /// <summary>The left control key.</summary>
    LeftControl,

    /// <summary>The left alt (option) key.</summary>
    LeftAlt,

    /// <summary>The left platform key — Windows, Command, or Meta.</summary>
    LeftSuper,

    /// <summary>The right shift key.</summary>
    RightShift,

    /// <summary>The right control key.</summary>
    RightControl,

    /// <summary>The right alt (option) key.</summary>
    RightAlt,

    /// <summary>The right platform key — Windows, Command, or Meta.</summary>
    RightSuper,

    /// <summary>The menu/application key.</summary>
    Menu,
}
