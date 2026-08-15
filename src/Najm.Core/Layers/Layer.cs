namespace Najm.Core;

/// <summary>Defines one coordinate-space root and its scene-level lifecycle.</summary>
public abstract class Layer
{
    private LayerStack? ownerStack;
    private LayerStack? reservationStack;
    private Scene? attachedScene;
    private Node? runtimeRoot;

    /// <summary>Gets or sets whether this layer participates in rendering.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets whether increasing Y points visually upward in this layer's coordinate space.</summary>
    public virtual bool YAxisPointsUp => false;

    /// <summary>Runs when this layer becomes attached to a loaded scene.</summary>
    protected virtual void OnAttach(Scene scene)
    {
    }

    /// <summary>Runs when this layer leaves its loaded scene.</summary>
    protected virtual void OnDetach()
    {
    }

    /// <summary>Updates this layer before its root subtree.</summary>
    protected virtual void Update(in TickContext tick)
    {
    }

    /// <summary>Gets the non-null, identity-stable permanent root used by this layer implementation.</summary>
    /// <remarks>The base class establishes root ownership when the layer first enters runtime use.</remarks>
    protected abstract Node RootNode { get; }

    internal Node RuntimeRoot
    {
        get
        {
            var current = RootNode;
            if (current is null)
            {
                throw new InvalidOperationException("A layer must expose a non-null permanent root node.");
            }

            if (runtimeRoot is null)
            {
                current.AssignLayerRoot(this);
                runtimeRoot = current;
            }
            else if (!ReferenceEquals(runtimeRoot, current))
            {
                throw new InvalidOperationException("A layer's permanent root node identity cannot change.");
            }

            return runtimeRoot;
        }
    }

    internal Node EstablishedRuntimeRoot => runtimeRoot ?? RuntimeRoot;

    internal LayerStack? OwnerStack => ownerStack;

    internal LayerStack? ReservationStack => reservationStack;

    internal Scene? AttachedScene => attachedScene;

    internal void SetOwnerStack(LayerStack? value) => ownerStack = value;

    internal void SetReservationStack(LayerStack? value) => reservationStack = value;

    internal void SetAttachedScene(Scene? value) => attachedScene = value;

    internal void InvokeAttach(Scene scene) => OnAttach(scene);

    internal void InvokeDetach() => OnDetach();

    internal void InvokeUpdate(in TickContext tick) => Update(tick);
}
