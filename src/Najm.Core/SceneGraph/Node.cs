using Najm.Utils;

namespace Najm.Core;

/// <summary>Provides identity, parenting, and insertion-ordered children for a scene-graph node.</summary>
/// <remarks>
/// A detached tree mutates immediately. While a tree is attached to a loaded
/// scene, or reserved by one of that scene's pending edits, the same public
/// <see cref="Add{T}(T)"/> and <see cref="Remove"/> calls are routed through the
/// scene runtime. Edits requested during Update are deferred until that phase
/// finishes; public topology remains unchanged until the queue flushes.
/// </remarks>
public abstract class Node
{
    private List<Node>? children;
    private BehaviorCollection? behaviors;
    private NodeChildren? childView;
    private Layer? layer;
    private Layer? layerRootOwner;
    private Node? parent;
    private INodeMutationSink? mutationSink;
    private INodeMutationSink? reservationSink;
    private int[]? paintOrder;
    private bool paintOrderIsInsertion = true;
    private bool paintOrderDirty = true;

    /// <summary>Gets this node's parent, or <see langword="null"/> while it is a root.</summary>
    public Node? Parent => parent;

    /// <summary>
    /// Gets a live, read-only, insertion-ordered view of this node's children.
    /// </summary>
    public NodeChildren Children => childView ??= new NodeChildren(this);

    /// <summary>Gets this node's controlled, attach-ordered behavior collection.</summary>
    public BehaviorCollection Behaviors => behaviors ??= new BehaviorCollection(this);

    /// <summary>Gets the owning layer while attached, or <see langword="null"/> while detached.</summary>
    public Layer? Layer => layer;

    /// <summary>Gets or sets whether this node and its subtree participate in Update.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets whether this node and its subtree participate in rendering.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Adds a detached child and returns it with its concrete type preserved.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="child"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The edit would create a cycle, duplicate a child, or give a child multiple parents.
    /// </exception>
    public T Add<T>(T child)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(child);

        var sink = mutationSink ?? reservationSink;
        if (sink is not null)
        {
            sink.RequestAdd(this, child);
        }
        else
        {
            AddImmediate(child);
        }

        return child;
    }

    /// <summary>
    /// Removes a direct child by identity. Returns false when the node is not a
    /// direct child; removing never searches descendants.
    /// </summary>
    public bool Remove(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);

        var sink = mutationSink ?? reservationSink;
        return sink is not null
            ? sink.RequestRemove(this, child)
            : RemoveImmediate(child);
    }

    /// <summary>Starts a node-lifetime coroutine and returns its handle.</summary>
    /// <param name="routine">
    /// The routine body. It is driven by the scene's coroutine pass, which runs once per tick inside
    /// Update after the whole tree has updated.
    /// </param>
    /// <remarks>
    /// <para>
    /// Node lifetime means the routine is cancelled when this node detaches from its scene, during
    /// the deferred flush that performs the detach, which disposes its enumerator and runs any
    /// <c>finally</c> the author wrote. It is also suspended — exactly <see cref="CoroutineHandle.Pause"/>
    /// semantics — while <see cref="Enabled"/> is false on this node or on any ancestor, and resumes
    /// in place when that stops being true.
    /// </para>
    /// <para>
    /// A node schedules through its scene, so it must already be attached to a loaded one;
    /// <see cref="OnAttach"/> is the natural place to start a routine that should live as long as the
    /// node does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="routine"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// This node is not attached to a scene that can schedule.
    /// </exception>
    public CoroutineHandle Start(IEnumerator<Wait> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        return RequireScheduler(nameof(Start)).Start(routine, this);
    }

    /// <summary>Starts a node-lifetime tween over a float property and returns its handle.</summary>
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">The easing curve. The default is <see cref="Ease.Linear"/>.</param>
    /// <remarks>
    /// The from-value is applied immediately; the first delta is consumed at the next tween pass.
    /// Detaching this node cancels the tween, which stops it at its current value rather than
    /// snapping it to either end, and disabling this node or an ancestor freezes its tween time until
    /// the subtree is enabled again.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An endpoint is not finite, or the duration is not finite and non-negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// This node is not attached to a scene that can schedule.
    /// </exception>
    public AnimationHandle Animate(
        Action<float> setter,
        float from,
        float to,
        double duration,
        TimingFunction ease = default) =>
        RequireScheduler(nameof(Animate)).Animate(setter, from, to, duration, ease, custom: null, owner: this);

    /// <inheritdoc cref="Animate(Action{float}, float, float, double, TimingFunction)" />
    /// <param name="setter">Receives the from-value now and every value the ramp produces after.</param>
    /// <param name="from">The value written synchronously, at this call site.</param>
    /// <param name="to">The exact value written when the tween completes.</param>
    /// <param name="duration">Finite, non-negative simulation seconds the ramp takes.</param>
    /// <param name="ease">A custom easing curve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="setter"/> or <paramref name="ease"/> is null.</exception>
    public AnimationHandle Animate(
        Action<float> setter,
        float from,
        float to,
        double duration,
        ITimingFunction ease)
    {
        ArgumentNullException.ThrowIfNull(ease);
        return RequireScheduler(nameof(Animate))
            .Animate(setter, from, to, duration, default, ease, owner: this);
    }

    internal int ChildCount => children?.Count ?? 0;

    internal int BehaviorCount => behaviors?.Count ?? 0;

    internal INodeMutationSink? MutationSink => mutationSink;

    internal INodeMutationSink? ReservationSink => reservationSink;

    internal Layer? LayerRootOwner => layerRootOwner;

    internal Node GetChild(int index) =>
        children is null ? throw new ArgumentOutOfRangeException(nameof(index)) : children[index];

    /// <summary>
    /// Returns the child at one position of this node's paint order: a stable sort of the
    /// insertion order by <see cref="Node2D.ZIndex"/>, with equal keys retaining insertion order.
    /// </summary>
    /// <remarks>
    /// The order is cached and rebuilt only after a child is added or removed or a child's paint
    /// key changes. When every child shares one key, which is the common case, the cache resolves
    /// to the insertion order itself and no index buffer is built, so warm traversal allocates
    /// nothing.
    /// </remarks>
    internal Node GetChildInPaintOrder(int index)
    {
        EnsurePaintOrder();
        if (paintOrderIsInsertion)
        {
            return GetChild(index);
        }
        if ((uint)index >= (uint)ChildCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return GetChild(paintOrder![index]);
    }

    internal void InvalidatePaintOrder() => paintOrderDirty = true;

    internal Behavior GetBehavior(int index) =>
        behaviors is null ? throw new ArgumentOutOfRangeException(nameof(index)) : behaviors[index];

    internal void AddImmediate(Node child)
    {
        ValidateAdd(child);

        children ??= [];
        children.Add(child);
        InvalidatePaintOrder();

        var previousParent = child.parent;
        child.parent = this;
        child.SetMutationSinkRecursively(mutationSink);
        child.OnParentChanged(previousParent, this);
    }

    internal bool RemoveImmediate(Node child)
    {
        if (!ReferenceEquals(child.parent, this))
        {
            return false;
        }

        var index = IndexOfChild(child);
        if (index < 0)
        {
            throw new InvalidOperationException("The node parent and child list are inconsistent.");
        }

        children!.RemoveAt(index);
        InvalidatePaintOrder();
        child.parent = null;
        child.SetMutationSinkRecursively(null);
        child.OnParentChanged(this, null);
        return true;
    }

    internal void SetMutationSinkRecursively(INodeMutationSink? sink)
    {
        mutationSink = sink;

        for (var index = 0; index < ChildCount; index++)
        {
            GetChild(index).SetMutationSinkRecursively(sink);
        }
    }

    internal void SetReservationSinkRecursively(INodeMutationSink? sink)
    {
        reservationSink = sink;

        for (var behaviorIndex = 0; behaviorIndex < BehaviorCount; behaviorIndex++)
        {
            GetBehavior(behaviorIndex).SetReservationSink(sink);
        }
        for (var childIndex = 0; childIndex < ChildCount; childIndex++)
        {
            GetChild(childIndex).SetReservationSinkRecursively(sink);
        }
    }

    internal void SetReservationSink(INodeMutationSink? sink) => reservationSink = sink;

    internal void SetLayerRecursively(Layer? value)
    {
        layer = value;

        for (var index = 0; index < ChildCount; index++)
        {
            GetChild(index).SetLayerRecursively(value);
        }
    }

    internal void AssignLayerRoot(Layer owner)
    {
        if (parent is not null)
        {
            throw new InvalidOperationException("A layer root cannot already have a parent.");
        }
        if (layerRootOwner is not null)
        {
            if (ReferenceEquals(layerRootOwner, owner))
            {
                return;
            }

            throw new InvalidOperationException("A node cannot be the permanent root of multiple layers.");
        }

        layerRootOwner = owner;
    }

    internal void InvokeAttach() => OnAttach();

    internal void InvokeDetach() => OnDetach();

    internal void InvokeUpdate(in TickContext tick) => Update(tick);

    internal void InvokeRender(IDrawContext2D context) => Render(context);

    /// <summary>Runs when this node becomes attached to a loaded scene.</summary>
    protected virtual void OnAttach()
    {
    }

    /// <summary>Runs when this node leaves its loaded scene.</summary>
    protected virtual void OnDetach()
    {
    }

    /// <summary>Updates this node before its behaviors and children.</summary>
    protected virtual void Update(in TickContext tick)
    {
    }

    /// <summary>Draws this node before its children. The default implementation draws nothing.</summary>
    /// <param name="context">
    /// The borrowed drawing surface, already carrying this node's resolved transform as installed
    /// by the render traverser. All drawing is therefore expressed in this node's local
    /// coordinates, and any transform pushed here composes below that one and must be balanced
    /// before returning.
    /// </param>
    /// <remarks>
    /// Render must not mutate observable scene state. It may not change transforms, properties, or
    /// tree topology, and it may not queue structural edits. Rendering the same tick twice must
    /// produce the same output, because the engine may render one ticked frame more than once.
    /// </remarks>
    public virtual void Render(IDrawContext2D context)
    {
    }

    internal virtual int PaintOrderKey => 0;

    internal virtual void OnParentChanged(Node? previousParent, Node? currentParent)
    {
    }

    /// <summary>Resolves the scheduler this node schedules through: its layer's scene's.</summary>
    private Scheduler RequireScheduler(string operation)
    {
        var scene = layer?.AttachedScene ?? throw new InvalidOperationException(
            $"Node.{operation} requires a node attached to a loaded scene.");
        return scene.RequireScheduler($"Node.{operation}");
    }

    private void EnsurePaintOrder()
    {
        if (!paintOrderDirty)
        {
            return;
        }

        paintOrderDirty = false;

        var count = ChildCount;
        if (count < 2 || ChildrenSharePaintKey(count))
        {
            paintOrderIsInsertion = true;
            return;
        }

        if (paintOrder is null || paintOrder.Length < count)
        {
            paintOrder = new int[count];
        }

        SortPaintOrder(paintOrder, count);
        paintOrderIsInsertion = false;
    }

    private bool ChildrenSharePaintKey(int count)
    {
        var first = children![0].PaintOrderKey;
        for (var index = 1; index < count; index++)
        {
            if (children[index].PaintOrderKey != first)
            {
                return false;
            }
        }

        return true;
    }

    private void SortPaintOrder(int[] buffer, int count)
    {
        var list = children!;
        for (var index = 0; index < count; index++)
        {
            buffer[index] = index;
        }

        for (var index = 1; index < count; index++)
        {
            var candidate = buffer[index];
            var key = list[candidate].PaintOrderKey;
            var position = index - 1;
            while (position >= 0 && list[buffer[position]].PaintOrderKey > key)
            {
                buffer[position + 1] = buffer[position];
                position--;
            }

            buffer[position + 1] = candidate;
        }
    }

    private void ValidateAdd(Node child)
    {
        if (SpaceKind != child.SpaceKind)
        {
            throw new InvalidOperationException(
                $"A {child.SpaceKind} node cannot be parented beneath a {SpaceKind} node.");
        }

        for (Node? ancestor = this; ancestor is not null; ancestor = ancestor.parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException("A node cannot be added to itself or one of its descendants.");
            }
        }

        if (child.parent is not null)
        {
            var message = ReferenceEquals(child.parent, this)
                ? "The node is already a child of this parent."
                : "The node already has a different parent.";
            throw new InvalidOperationException(message);
        }

        if (child.layerRootOwner is not null)
        {
            throw new InvalidOperationException("A layer root cannot be parented beneath another node.");
        }

        var ownerSink = mutationSink ?? reservationSink;
        if (child.mutationSink is not null && !ReferenceEquals(child.mutationSink, ownerSink))
        {
            throw new InvalidOperationException("An attached node cannot be added to a detached or different tree.");
        }
        if (child.reservationSink is not null && !ReferenceEquals(child.reservationSink, ownerSink))
        {
            throw new InvalidOperationException("A node reserved by a scene mutation cannot be claimed elsewhere.");
        }
    }

    private int IndexOfChild(Node child)
    {
        if (children is null)
        {
            return -1;
        }

        for (var index = 0; index < children.Count; index++)
        {
            if (ReferenceEquals(children[index], child))
            {
                return index;
            }
        }

        return -1;
    }

    internal virtual NodeSpaceKind SpaceKind => NodeSpaceKind.None;
}

internal enum NodeSpaceKind : byte
{
    None,
    TwoD,
}
