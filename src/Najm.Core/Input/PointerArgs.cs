using System.Numerics;

namespace Najm.Core;

/// <summary>Describes a routed pointer event in both of the spaces a handler needs.</summary>
/// <remarks>
/// <para>
/// ARCHITECTURE §9.3 makes the coordinate spaces explicit and requires both to be delivered,
/// computed at dispatch: <see cref="Virtual"/> is the scene's presentation space (§3.3) and
/// <see cref="Local"/> is the receiving node's own. Handing over only one would push the conversion
/// into every handler, and the conversion is the part that is easy to get wrong — it runs through
/// the layer's camera and viewport, not through <see cref="Node2D.InverseWorld"/>, because §6.3
/// keeps camera resolution out of the world matrix.
/// </para>
/// <para>
/// <strong>The deltas are why dragging is one attach and not a per-demo reinvention.</strong>
/// <see cref="LocalDelta"/> is this pointer's movement expressed in the receiving node's units, so
/// a handler that adds it to <see cref="Node2D.Position"/> tracks the pointer exactly — at any
/// camera zoom, under any rotated ancestor, without the handler knowing either exists. The naive
/// alternative, converting two virtual positions through the node's own inverse world matrix,
/// silently drifts under a moving camera.
/// </para>
/// </remarks>
public readonly struct PointerArgs
{
    internal PointerArgs(
        in InputEvent source,
        Vector2 local,
        Vector2 virtualDelta,
        Vector2 localDelta)
    {
        Virtual = source.Position;
        Local = local;
        VirtualDelta = virtualDelta;
        LocalDelta = localDelta;
        PointerId = source.PointerId;
        Button = source.Button;
        Buttons = source.Buttons;
        Modifiers = source.Modifiers;
        Scroll = source.Scroll;
    }

    /// <summary>Gets the pointer position in scene virtual coordinates (§3.3), unclamped.</summary>
    public Vector2 Virtual { get; }

    /// <summary>Gets the pointer position in the receiving node's local space.</summary>
    public Vector2 Local { get; }

    /// <summary>
    /// Gets how far this pointer moved since its previous event, in virtual coordinates. Zero on a
    /// pointer's first event of a run.
    /// </summary>
    public Vector2 VirtualDelta { get; }

    /// <summary>
    /// Gets the same movement expressed in the receiving node's local units — the value a drag
    /// handler adds to a position.
    /// </summary>
    /// <remarks>
    /// This is the difference of two <em>points</em> carried through the node's resolved mapping,
    /// so it already accounts for camera zoom, rotation, and every scale in the node's chain.
    /// </remarks>
    public Vector2 LocalDelta { get; }

    /// <summary>Gets the pointer's identity: one stable value for a mouse, one per touch contact.</summary>
    public int PointerId { get; }

    /// <summary>
    /// Gets the single button this event is about, or <see cref="PointerButton.None"/> for a move or
    /// a scroll.
    /// </summary>
    public PointerButton Button { get; }

    /// <summary>Gets every button held during this event.</summary>
    public PointerButton Buttons { get; }

    /// <summary>Gets the modifier keys held when the event was produced.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Gets the scroll delta in notches on a scroll event, and zero on every other kind — §9.3's
    /// "scroll delta where applicable".
    /// </summary>
    public Vector2 Scroll { get; }
}
