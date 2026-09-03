using System.Numerics;
using System.Text;
using Najm.Core;
using Silk.NET.Input;
using Key = Najm.Core.Key;
using SilkKey = Silk.NET.Input.Key;

namespace Najm.Host.Desktop;

/// <summary>Drains the window's input devices into one <see cref="InputBuffer"/>, in virtual coordinates.</summary>
/// <remarks>
/// <para>
/// This is §4.6's "before each tick it synchronizes platform input … into Core abstractions" and
/// §9.1's "hosts translate platform events and inverse-letterbox pointer coordinates into virtual
/// space before the scene ever sees them", and it is the entire scope of the class: translate,
/// map, push. It decides nothing about what the input means.
/// </para>
/// <para>
/// <strong>The events arrive during the pump, not on a queue of our own.</strong> Silk.NET raises
/// them synchronously inside <c>DoEvents</c>, on the thread that called it, which is the thread that
/// ticks — so §3.5 holds without a hand-off and <see cref="InputBuffer"/>'s "not thread-safe" note
/// costs nothing. The host calls <see cref="InputBuffer.BeginFrame"/> before the pump; everything
/// these handlers push therefore lands in the block the following tick reads.
/// </para>
/// <para>
/// <strong>Auto-repeat is inferred, because the platform does not report it.</strong> Silk.NET
/// raises the same <c>KeyDown</c> for a fresh press and for the operating system's repeat. A key
/// already held when another press arrives is a repeat by definition, and the buffer's own held-key
/// snapshot is what answers that — no second copy of the keyboard state.
/// </para>
/// </remarks>
internal sealed class WindowInput : IDisposable
{
    private readonly IInputContext context;
    private readonly InputBuffer buffer;
    private readonly Func<Vector2, Vector2> toVirtual;
    private readonly Action<Key> onReservedKey;
    private readonly HashSet<IInputDevice> attached = [];
    private char pendingHighSurrogate;
    private bool disposed;

    /// <summary>Attaches to every device the context currently has, and to any that arrives later.</summary>
    /// <param name="context">The window's input context. Disposal of it stays with the host.</param>
    /// <param name="buffer">The buffer this frame's events are pushed into.</param>
    /// <param name="toVirtual">
    /// The window-to-virtual mapping, read per event rather than captured, so a resize between two
    /// events is honoured by the second.
    /// </param>
    /// <param name="onReservedKey">
    /// Called with a key the buffer refused because the host reserved it (§9.1). The buffer records
    /// nothing for it, so this is the only notice the host gets that it was pressed.
    /// </param>
    internal WindowInput(
        IInputContext context,
        InputBuffer buffer,
        Func<Vector2, Vector2> toVirtual,
        Action<Key> onReservedKey)
    {
        this.context = context;
        this.buffer = buffer;
        this.toVirtual = toVirtual;
        this.onReservedKey = onReservedKey;

        foreach (var keyboard in context.Keyboards)
        {
            Attach(keyboard);
        }

        foreach (var mouse in context.Mice)
        {
            Attach(mouse);
        }

        context.ConnectionChanged += OnConnectionChanged;
    }

    /// <summary>Clears held keys, held buttons, and the pointer position for a lost focus.</summary>
    /// <remarks>
    /// A window that loses focus with a key down never sees the release — it goes to whoever took
    /// focus — and the scene would believe that key held forever. <see cref="InputBuffer.ResetState"/>
    /// says the same thing from the other side; this is the moment it exists for.
    /// </remarks>
    internal void ReleaseHeldState()
    {
        pendingHighSurrogate = '\0';
        buffer.ResetState();
    }

    /// <summary>Detaches every handler this instance installed.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        context.ConnectionChanged -= OnConnectionChanged;
        foreach (var device in attached)
        {
            Detach(device);
        }

        attached.Clear();
    }

    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (connected)
        {
            Attach(device);
        }
        else if (attached.Remove(device))
        {
            Detach(device);
        }
    }

    private void Attach(IInputDevice device)
    {
        if (!attached.Add(device))
        {
            return;
        }

        switch (device)
        {
            case IKeyboard keyboard:
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
                keyboard.KeyChar += OnKeyChar;
                break;
            case IMouse mouse:
                mouse.MouseMove += OnMouseMove;
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.Scroll += OnScroll;
                break;
            default:
                // Gamepads and joysticks are §9.4 deferred; nothing here listens to them.
                attached.Remove(device);
                break;
        }
    }

    private void Detach(IInputDevice device)
    {
        switch (device)
        {
            case IKeyboard keyboard:
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
                keyboard.KeyChar -= OnKeyChar;
                break;
            case IMouse mouse:
                mouse.MouseMove -= OnMouseMove;
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.Scroll -= OnScroll;
                break;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, SilkKey key, int scancode)
    {
        var mapped = PlatformInputMap.ToKey(key);
        var isRepeat = buffer.Block.IsDown(mapped);
        if (!buffer.PressKey(mapped, Modifiers(), isRepeat))
        {
            onReservedKey(mapped);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, SilkKey key, int scancode) =>
        buffer.ReleaseKey(PlatformInputMap.ToKey(key), Modifiers());

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        // Silk.NET delivers a UTF-16 unit, so a code point above the BMP arrives as two calls.
        // Rune refuses an unpaired surrogate, and a text event for half a character would be worse
        // than none, so the high half waits for its partner.
        if (char.IsHighSurrogate(character))
        {
            pendingHighSurrogate = character;
            return;
        }

        if (pendingHighSurrogate != '\0')
        {
            var high = pendingHighSurrogate;
            pendingHighSurrogate = '\0';
            if (char.IsLowSurrogate(character))
            {
                buffer.EnterText(new Rune(high, character), Modifiers());
                return;
            }
        }

        if (!char.IsSurrogate(character))
        {
            buffer.EnterText(new Rune(character), Modifiers());
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position) =>
        buffer.MovePointer(mouse.Index, toVirtual(position), Modifiers());

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        var mapped = PlatformInputMap.ToButton(button);
        if (mapped != PointerButton.None)
        {
            buffer.PressPointer(mouse.Index, toVirtual(mouse.Position), mapped, Modifiers());
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        var mapped = PlatformInputMap.ToButton(button);
        if (mapped != PointerButton.None)
        {
            buffer.ReleasePointer(mouse.Index, toVirtual(mouse.Position), mapped, Modifiers());
        }
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel) =>
        buffer.ScrollPointer(
            mouse.Index,
            toVirtual(mouse.Position),
            new Vector2(wheel.X, wheel.Y),
            Modifiers());

    /// <summary>Reads the modifier keys held right now, across every attached keyboard.</summary>
    /// <remarks>
    /// Silk.NET's key events carry no modifier state, so it is polled at dispatch. Every keyboard is
    /// consulted because a presenter's clicker is a second keyboard and shift held on either one is
    /// shift held.
    /// </remarks>
    private KeyModifiers Modifiers()
    {
        var modifiers = KeyModifiers.None;
        var keyboards = context.Keyboards;
        for (var index = 0; index < keyboards.Count; index++)
        {
            var keyboard = keyboards[index];
            if (keyboard.IsKeyPressed(SilkKey.ShiftLeft) || keyboard.IsKeyPressed(SilkKey.ShiftRight))
            {
                modifiers |= KeyModifiers.Shift;
            }

            if (keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight))
            {
                modifiers |= KeyModifiers.Control;
            }

            if (keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight))
            {
                modifiers |= KeyModifiers.Alt;
            }

            if (keyboard.IsKeyPressed(SilkKey.SuperLeft) || keyboard.IsKeyPressed(SilkKey.SuperRight))
            {
                modifiers |= KeyModifiers.Super;
            }
        }

        return modifiers;
    }
}
