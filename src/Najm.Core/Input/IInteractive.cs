namespace Najm.Core;

/// <summary>Opts a 2D node into routed input.</summary>
/// <remarks>
/// <para>
/// ARCHITECTURE §9.3: <strong>valid on any node in any 2D layer</strong>, not only on widgets. A
/// draggable point in a <see cref="WorldLayer2D"/> is as first-class as a slider in a
/// <see cref="ScreenLayer"/>, because the router's mapping (§9.2) converts into whatever local
/// space the receiver happens to sit in — the handler never learns which.
/// </para>
/// <para>
/// <strong>Every member has a default, so implementing this costs exactly the members you want.</strong>
/// A node that only wants clicks writes one method. The default answers are "did nothing"
/// (<c>false</c>) and "no reaction" (empty), so a partial implementation never silently swallows
/// input it did not handle.
/// </para>
/// <para>
/// <strong>Returning true consumes the event.</strong> A consumed event disappears from every
/// polling accessor on <see cref="InputBlock"/> for the rest of the frame (§9.1), which is how a
/// button stops the scene beneath it from also reacting to the same click. Returning false leaves
/// the event for whoever is next. The notification members — enter, exit, focus, blur — return
/// nothing, because they report a state change rather than deliver an event there is anything to
/// consume.
/// </para>
/// <para>
/// <strong>Hit testing is not here.</strong> §9.3 takes "<c>HitTest</c>/bounds from the drawable
/// contract": a node states its own gate through <see cref="Node2D.HitBounds"/> and
/// <see cref="Node2D.HitTest(System.Numerics.Vector2)"/> whether or not it is interactive, and the
/// router asks those before it asks anything here.
/// </para>
/// <para>
/// <strong>Polling is the alternative, and it is a lesser one for gestures.</strong> §9.3 keeps
/// both — hybrid routing by design — but polling cannot participate in capture or consumption, so
/// anything drag-shaped belongs on the router.
/// </para>
/// </remarks>
public interface IInteractive
{
    /// <summary>Runs when a pointer starts hovering this node.</summary>
    /// <param name="args">The pointer event that carried the pointer onto this node.</param>
    /// <remarks>
    /// Hover follows the captured node while a capture is active, so a drag that leaves the node
    /// does not report an exit until the capture ends.
    /// </remarks>
    void OnPointerEnter(in PointerArgs args)
    {
    }

    /// <summary>Runs when a pointer stops hovering this node.</summary>
    /// <param name="args">The pointer event that carried the pointer away.</param>
    void OnPointerExit(in PointerArgs args)
    {
    }

    /// <summary>Runs when a pointer button goes down on this node.</summary>
    /// <param name="args">The press, in both spaces.</param>
    /// <returns>True to consume the event.</returns>
    /// <remarks>
    /// This is where a drag begins: call <see cref="InputRouter.Capture"/> here and every later move
    /// of that pointer arrives at this node as <see cref="OnDrag"/>, wherever the pointer travels.
    /// Nothing takes keyboard focus automatically — a node that wants it calls
    /// <see cref="InputRouter.Focus"/> from here, which keeps click-to-focus a component's policy
    /// rather than the engine's.
    /// </remarks>
    bool OnPointerDown(in PointerArgs args) => false;

    /// <summary>Runs when a pointer button comes up on this node, or anywhere while it holds capture.</summary>
    /// <param name="args">The release, in both spaces.</param>
    /// <returns>True to consume the event.</returns>
    bool OnPointerUp(in PointerArgs args) => false;

    /// <summary>Runs when a pointer moves over this node with no button held.</summary>
    /// <param name="args">The move, in both spaces, carrying the deltas.</param>
    /// <returns>True to consume the event.</returns>
    bool OnPointerMove(in PointerArgs args) => false;

    /// <summary>Runs when a pointer this node has captured moves with a button held.</summary>
    /// <param name="args">The move, with <see cref="PointerArgs.LocalDelta"/> in this node's units.</param>
    /// <returns>True to consume the event.</returns>
    /// <remarks>
    /// A move is either a drag or a plain move, never both: with capture and a held button it
    /// arrives here, and otherwise at <see cref="OnPointerMove"/>. Capture is what makes the
    /// distinction meaningful — without it a drag would stop the moment the pointer left the node.
    /// </remarks>
    bool OnDrag(in PointerArgs args) => false;

    /// <summary>Runs when the wheel turns over this node.</summary>
    /// <param name="args">The event, with the delta in <see cref="PointerArgs.Scroll"/>.</param>
    /// <returns>True to consume the event.</returns>
    bool OnScroll(in PointerArgs args) => false;

    /// <summary>Runs when this node becomes the scene's keyboard focus.</summary>
    void OnFocus()
    {
    }

    /// <summary>Runs when this node stops being the scene's keyboard focus.</summary>
    /// <remarks>
    /// Detaching a focused node clears the focus without running this: the node is already leaving,
    /// and <see cref="Node.OnDetach"/> is the hook that says so.
    /// </remarks>
    void OnBlur()
    {
    }

    /// <summary>Runs on a key press or release while this node holds focus.</summary>
    /// <param name="args">The key, its direction, and the modifiers.</param>
    /// <returns>True to consume the event.</returns>
    bool OnKey(in KeyArgs args) => false;

    /// <summary>Runs on a typed character while this node holds focus.</summary>
    /// <param name="args">The layout-resolved character.</param>
    /// <returns>True to consume the event.</returns>
    bool OnTextInput(in TextInputArgs args) => false;
}
