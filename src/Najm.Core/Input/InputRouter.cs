using System.Numerics;

namespace Najm.Core;

/// <summary>Dispatches one scene's input to its nodes during the Input phase.</summary>
/// <remarks>
/// <para>
/// <strong>The order is the contract.</strong> ARCHITECTURE §9.2: captured and focused targets are
/// dispatched first; otherwise each input-participating layer is visited <strong>top-to-bottom</strong>
/// and its node tree in <strong>exact reverse paint order</strong>. Paint order is §6.5's stable
/// sort by <c>(ZIndex, insertion index)</c>, depth-first pre-order with parents beneath children, so
/// its exact reverse is: each node's children last-to-first, then the node itself. What is drawn on
/// top is asked first, which is the only rule under which what a user sees and what a user clicks
/// are the same thing.
/// </para>
/// <para>
/// <strong>The gate is two steps, and both matter.</strong> A node is a candidate when its
/// <em>resolved</em> hit bounds contain the pointer — resolved meaning through the layer's camera,
/// viewport, and scale mode (§6.6, §9.2), never through a camera-free <c>InverseWorld</c> — and
/// then when <see cref="Node2D.HitTest(Vector2)"/> accepts the point in local space. The first step
/// is a rectangle test that rejects almost everything; the second is where a disc gets to be a disc.
/// </para>
/// <para>
/// <strong>What stops the walk.</strong> <see cref="Node.Enabled"/> false skips the subtree for
/// input exactly as it does for update (§6.1, §6.5); <see cref="Node.Visible"/> false skips it for
/// hit testing exactly as for render; <see cref="Node2D.Clip"/> gates it, tested exactly in the
/// node's own local coordinates. Masks and effects do not gate anything (§9.2) — a glow is not a
/// bigger button.
/// </para>
/// <para>
/// <strong>Capture and focus bypass the walk.</strong> A pointer with a captured target goes
/// straight there, wherever it is on screen, until the capture is released — that is what makes a
/// drag survive leaving the node. Keys and typed characters go to the focused node and nowhere
/// else. Detaching a node releases both, silently and deterministically (§6.4, §6.6).
/// </para>
/// <para>
/// <strong>Deterministic runs idle here.</strong> An empty block (§9.1) returns before anything is
/// read, so a fixed-step render costs one branch and observes nothing — which is the whole of §2.1's
/// promise as it applies to this type. <see cref="Scene.Tick(in TickContext)"/> enforces the other
/// half by refusing a fixed-step tick that carries input at all.
/// </para>
/// <para>
/// A warm frame allocates nothing: per-pointer state lives in an array that grows only when a new
/// pointer id appears, dispatch arguments are structs passed by <c>in</c>, and the walk is plain
/// recursion.
/// </para>
/// </remarks>
public sealed class InputRouter
{
    private readonly Scene scene;
    private PointerState[] pointers = new PointerState[4];
    private int pointerCount;
    private Node2D? focused;

    internal InputRouter(Scene scene) => this.scene = scene;

    /// <summary>Gets the node currently receiving keys and typed characters, or null.</summary>
    public Node2D? Focused => focused;

    /// <summary>Returns whether a layer's tree is walked for input.</summary>
    /// <param name="layer">The layer to test.</param>
    /// <remarks>
    /// The input counterpart of <see cref="RenderTraverser.ParticipatesInRender(Layer)"/>, and
    /// deliberately not the same predicate: a layer at zero opacity still receives input, while an
    /// invisible one does not. See <see cref="Layer.ReceivesInput"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is null.</exception>
    public static bool ParticipatesInInput(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return layer.ReceivesInput && layer.Visible;
    }

    /// <summary>Returns the topmost node a virtual-space point hits, or null.</summary>
    /// <param name="pointerVirtual">The point, in scene virtual coordinates (§3.3).</param>
    /// <remarks>
    /// The same walk the router runs, exposed because "what is under here" is a question tools,
    /// tests, and behaviors ask outside of an event. Only nodes implementing
    /// <see cref="IInteractive"/> are candidates: a node that cannot receive anything does not
    /// block one that can, which is what makes §9.3's opt-in mean something.
    /// </remarks>
    public Node2D? Pick(Vector2 pointerVirtual)
    {
        var layers = scene.Layers;
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            var layer = layers[index];
            if (!ParticipatesInInput(layer))
            {
                continue;
            }

            // A viewport'd layer occupies a region of the frame, and §9.2 maps the pointer through
            // it: a point outside the region never belonged to this layer at all.
            if (layer.Viewport is { } viewport && !viewport.Contains(pointerVirtual))
            {
                continue;
            }

            var layerBase = layer.VirtualBase;
            var hit = Pick(layer.EstablishedRuntimeRoot, layerBase, pointerVirtual);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>Gets the node a pointer is hovering, or null.</summary>
    /// <param name="pointerId">The pointer's identity.</param>
    public Node2D? HoverTarget(int pointerId) =>
        TryFindPointer(pointerId, out var index) ? pointers[index].Hovered : null;

    /// <summary>Gets the node holding a pointer's capture, or null.</summary>
    /// <param name="pointerId">The pointer's identity.</param>
    public Node2D? CaptureHolder(int pointerId) =>
        TryFindPointer(pointerId, out var index) ? pointers[index].Captured : null;

    /// <summary>Routes every later event from one pointer to a node until the capture is released.</summary>
    /// <param name="node">The node that will receive the pointer, which must implement <see cref="IInteractive"/>.</param>
    /// <param name="pointerId">The pointer to capture.</param>
    /// <remarks>
    /// Called from <see cref="IInteractive.OnPointerDown"/> in practice. Capture is what turns a
    /// press plus some moves into a drag: without it the moves stop arriving the instant the pointer
    /// leaves the node, which is the bug every hand-rolled drag has. Capturing a pointer another
    /// node already holds takes it over.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The node does not implement <see cref="IInteractive"/>, or does not belong to this scene.
    /// </exception>
    public void Capture(Node2D node, int pointerId)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is not IInteractive)
        {
            throw new ArgumentException(
                "Only an IInteractive node can capture a pointer; a node that implements nothing " +
                "would receive the captured events and drop them.",
                nameof(node));
        }
        if (!ReferenceEquals(node.Scene, scene))
        {
            throw new ArgumentException(
                "A node captures a pointer from the scene it is attached to. This node belongs to " +
                "another scene or to none.",
                nameof(node));
        }

        ref var state = ref RequirePointer(pointerId);
        state.Captured = node;
    }

    /// <summary>Releases a pointer's capture, if it has one.</summary>
    /// <param name="pointerId">The pointer to release.</param>
    /// <returns>True when a capture was released.</returns>
    public bool ReleaseCapture(int pointerId)
    {
        if (!TryFindPointer(pointerId, out var index) || pointers[index].Captured is null)
        {
            return false;
        }

        pointers[index].Captured = null;
        return true;
    }

    /// <summary>Moves keyboard focus, blurring whatever held it.</summary>
    /// <param name="node">
    /// The node to focus, which must implement <see cref="IInteractive"/> and belong to this scene,
    /// or null to focus nothing.
    /// </param>
    /// <remarks>
    /// The field is updated before either notification runs, so a handler reading
    /// <see cref="Focused"/> sees the new answer rather than a half-applied one. Focusing the node
    /// that already holds focus does nothing at all.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The node does not implement <see cref="IInteractive"/>, or does not belong to this scene.
    /// </exception>
    public void Focus(Node2D? node)
    {
        if (node is not null)
        {
            if (node is not IInteractive)
            {
                throw new ArgumentException(
                    "Only an IInteractive node can hold focus; keys delivered to anything else " +
                    "would have nowhere to go.",
                    nameof(node));
            }
            if (!ReferenceEquals(node.Scene, scene))
            {
                throw new ArgumentException(
                    "A node takes focus in the scene it is attached to. This node belongs to " +
                    "another scene or to none.",
                    nameof(node));
            }
        }

        if (ReferenceEquals(focused, node))
        {
            return;
        }

        var previous = focused;
        focused = node;
        (previous as IInteractive)?.OnBlur();
        (node as IInteractive)?.OnFocus();
    }

    /// <summary>Dispatches one tick's input. This is §4.7's Input phase.</summary>
    internal void Route(in TickContext tick)
    {
        var block = tick.Input;
        if (block.IsEmpty)
        {
            return;
        }

        for (var index = 0; index < block.EventCount; index++)
        {
            if (block.IsConsumed(index))
            {
                continue;
            }

            var value = block[index];
            var handled = value.Kind switch
            {
                InputEventKind.PointerMove or
                InputEventKind.PointerDown or
                InputEventKind.PointerUp or
                InputEventKind.Scroll => DispatchPointer(value),
                InputEventKind.KeyDown or
                InputEventKind.KeyUp => DispatchKey(value),
                InputEventKind.Text => DispatchText(value),
                _ => false,
            };

            if (handled)
            {
                block.Consume(index);
            }
        }
    }

    /// <summary>
    /// Drops the capture, focus, and hover a detaching subtree holds, without notifying it.
    /// </summary>
    /// <remarks>
    /// §6.4 and §6.6 require detach to release capture and focus deterministically. It happens
    /// silently: the subtree's <c>OnDetach</c> has already run by this point, and calling
    /// <c>OnBlur</c> or <c>OnPointerExit</c> into a node that is being removed would be a callback
    /// about a state nobody can act on.
    /// </remarks>
    internal void ReleaseSubtree(Node root)
    {
        if (focused is not null && IsWithin(focused, root))
        {
            focused = null;
        }

        for (var index = 0; index < pointerCount; index++)
        {
            ref var state = ref pointers[index];
            if (state.Captured is not null && IsWithin(state.Captured, root))
            {
                state.Captured = null;
            }
            if (state.Hovered is not null && IsWithin(state.Hovered, root))
            {
                state.Hovered = null;
            }
        }
    }

    private static bool IsWithin(Node node, Node root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static Node2D? Pick(Node node, in Matrix3x2 layerBase, Vector2 pointerVirtual)
    {
        // Enabled skips the subtree for input exactly as it does for update (§6.1); Visible skips it
        // for hit testing exactly as it does for render.
        if (!node.Enabled || !node.Visible)
        {
            return null;
        }

        var spatial = node as Node2D;
        var localToVirtual = spatial is not null ? spatial.WorldMatrix * layerBase : layerBase;

        // §9.2: Clip gates the walk. It is stated in the node's own local coordinates, so the exact
        // test is to carry the pointer down and compare there — no conservative hull, and no
        // pretending a rotated clip is axis-aligned.
        if (spatial?.Clip is { } clip)
        {
            if (!Matrix3x2.Invert(localToVirtual, out var toLocal) ||
                !clip.Contains(Vector2.Transform(pointerVirtual, toLocal)))
            {
                return null;
            }
        }

        // Exact reverse paint order: the last-painted child is asked first, and the node itself —
        // which paints beneath all of them — is asked after every descendant.
        for (var index = node.ChildCount - 1; index >= 0; index--)
        {
            var hit = Pick(node.GetChildInPaintOrder(index), layerBase, pointerVirtual);
            if (hit is not null)
            {
                return hit;
            }
        }

        if (spatial is not IInteractive)
        {
            return null;
        }

        var frame = Layer.ResolveComposed(spatial, localToVirtual);
        return frame.HitBoundsVirtual.Contains(pointerVirtual) &&
            frame.IsMappable &&
            spatial.HitTest(frame.VirtualToLocal(pointerVirtual))
            ? spatial
            : null;
    }

    private bool DispatchPointer(in InputEvent value)
    {
        ref var state = ref RequirePointer(value.PointerId);
        var virtualDelta = state.HasPosition ? value.Position - state.LastPosition : Vector2.Zero;
        state.LastPosition = value.Position;
        state.HasPosition = true;

        // Capture bypasses the walk (§9.2). Hover follows the captured node while it lasts, so a
        // drag that wanders off the node does not report an exit half way through.
        var captured = state.Captured;
        var target = captured ?? Pick(value.Position);

        UpdateHover(ref state, target, value, virtualDelta);

        if (target is not IInteractive receiver)
        {
            return false;
        }

        var args = BuildArgs(target, value, virtualDelta);
        return value.Kind switch
        {
            InputEventKind.PointerDown => receiver.OnPointerDown(args),
            InputEventKind.PointerUp => receiver.OnPointerUp(args),
            InputEventKind.Scroll => receiver.OnScroll(args),
            InputEventKind.PointerMove => captured is not null && value.Buttons != PointerButton.None
                ? receiver.OnDrag(args)
                : receiver.OnPointerMove(args),
            _ => false,
        };
    }

    private void UpdateHover(
        ref PointerState state,
        Node2D? target,
        in InputEvent value,
        Vector2 virtualDelta)
    {
        var previous = state.Hovered;
        if (ReferenceEquals(previous, target))
        {
            return;
        }

        state.Hovered = target;
        if (previous is IInteractive left)
        {
            left.OnPointerExit(BuildArgs(previous, value, virtualDelta));
        }
        if (target is IInteractive entered)
        {
            entered.OnPointerEnter(BuildArgs(target, value, virtualDelta));
        }
    }

    private static PointerArgs BuildArgs(Node2D node, in InputEvent value, Vector2 virtualDelta)
    {
        // A node whose layer is gone — detached mid-frame — cannot be resolved, and a drag that
        // survives into that frame gets its own local coordinates rather than a throw.
        if (node.Layer is not { } layer || layer.ResolvedScene is null)
        {
            return new PointerArgs(value, value.Position, virtualDelta, virtualDelta);
        }

        var frame = Layer.Resolve(node, layer.VirtualBase);
        if (!frame.IsMappable)
        {
            return new PointerArgs(value, Vector2.Zero, virtualDelta, Vector2.Zero);
        }

        var local = frame.VirtualToLocal(value.Position);

        // The delta is the difference of two mapped points, not a mapped difference: that is what
        // makes it right under a translating camera as well as a scaling one.
        var localDelta = local - frame.VirtualToLocal(value.Position - virtualDelta);
        return new PointerArgs(value, local, virtualDelta, localDelta);
    }

    private bool DispatchKey(in InputEvent value) =>
        focused is IInteractive receiver && receiver.OnKey(new KeyArgs(value));

    private bool DispatchText(in InputEvent value) =>
        focused is IInteractive receiver && receiver.OnTextInput(new TextInputArgs(value));

    private bool TryFindPointer(int pointerId, out int index)
    {
        for (index = 0; index < pointerCount; index++)
        {
            if (pointers[index].Id == pointerId)
            {
                return true;
            }
        }

        return false;
    }

    private ref PointerState RequirePointer(int pointerId)
    {
        if (TryFindPointer(pointerId, out var index))
        {
            return ref pointers[index];
        }

        // Grows once per distinct pointer id, which is a transition cost (§3.6) and not a per-frame
        // one: a mouse reaches steady state on its first event.
        if (pointerCount == pointers.Length)
        {
            Array.Resize(ref pointers, pointers.Length * 2);
        }

        pointers[pointerCount] = new PointerState { Id = pointerId };
        return ref pointers[pointerCount++];
    }

    private struct PointerState
    {
        internal int Id;
        internal Node2D? Captured;
        internal Node2D? Hovered;
        internal Vector2 LastPosition;
        internal bool HasPosition;
    }
}
