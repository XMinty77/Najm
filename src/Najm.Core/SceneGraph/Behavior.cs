namespace Najm.Core;

/// <summary>Provides transform-free logic owned by one scene-graph node.</summary>
public abstract class Behavior
{
    private Node? node;
    private INodeMutationSink? reservationSink;

    /// <summary>Gets the owning node, or <see langword="null"/> while this behavior is unowned.</summary>
    public Node? Node => node;

    /// <summary>Runs when the owning node becomes attached to a loaded scene.</summary>
    protected virtual void OnAttach()
    {
    }

    /// <summary>Runs when the owning node leaves its loaded scene.</summary>
    protected virtual void OnDetach()
    {
    }

    /// <summary>Updates this behavior after its owning node.</summary>
    protected virtual void Update(in TickContext tick)
    {
    }

    internal INodeMutationSink? ReservationSink => reservationSink;

    internal void SetOwner(Node? owner) => node = owner;

    internal void SetReservationSink(INodeMutationSink? sink) => reservationSink = sink;

    internal void InvokeAttach() => OnAttach();

    internal void InvokeDetach() => OnDetach();

    internal void InvokeUpdate(in TickContext tick) => Update(tick);
}
