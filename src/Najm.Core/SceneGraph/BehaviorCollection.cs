using System.Collections;

namespace Najm.Core;

/// <summary>Provides a controlled, attach-ordered collection of node behaviors.</summary>
public sealed class BehaviorCollection : IReadOnlyList<Behavior>
{
    private readonly Node owner;
    private List<Behavior>? items;

    internal BehaviorCollection(Node owner) => this.owner = owner;

    /// <inheritdoc />
    public Behavior this[int index] =>
        items is null ? throw new ArgumentOutOfRangeException(nameof(index)) : items[index];

    /// <inheritdoc />
    public int Count => items?.Count ?? 0;

    /// <summary>Adds an unowned behavior and returns it with its concrete type preserved.</summary>
    public T Add<T>(T behavior)
        where T : Behavior
    {
        ArgumentNullException.ThrowIfNull(behavior);

        var sink = owner.MutationSink ?? owner.ReservationSink;
        if (sink is not null)
        {
            sink.RequestAddBehavior(owner, behavior);
        }
        else
        {
            AddImmediate(behavior);
        }

        return behavior;
    }

    /// <summary>Removes a behavior by identity.</summary>
    public bool Remove(Behavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);

        var sink = owner.MutationSink ?? owner.ReservationSink;
        return sink is not null
            ? sink.RequestRemoveBehavior(owner, behavior)
            : RemoveImmediate(behavior);
    }

    /// <summary>Returns an allocation-free enumerator over attach order.</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<Behavior> IEnumerable<Behavior>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void AddImmediate(Behavior behavior)
    {
        if (behavior.Node is not null)
        {
            var message = ReferenceEquals(behavior.Node, owner)
                ? "The behavior is already owned by this node."
                : "The behavior is already owned by a different node.";
            throw new InvalidOperationException(message);
        }
        var ownerSink = owner.MutationSink ?? owner.ReservationSink;
        if (behavior.ReservationSink is not null &&
            !ReferenceEquals(behavior.ReservationSink, ownerSink))
        {
            throw new InvalidOperationException("A behavior reserved by a scene mutation cannot be claimed elsewhere.");
        }

        items ??= [];
        items.Add(behavior);
        behavior.SetOwner(owner);
    }

    internal bool RemoveImmediate(Behavior behavior)
    {
        if (!ReferenceEquals(behavior.Node, owner))
        {
            return false;
        }

        var index = IndexOf(behavior);
        if (index < 0)
        {
            throw new InvalidOperationException("The behavior owner and collection are inconsistent.");
        }

        items!.RemoveAt(index);
        behavior.SetOwner(null);
        return true;
    }

    private int IndexOf(Behavior behavior)
    {
        if (items is null)
        {
            return -1;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], behavior))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Enumerates one concrete behavior collection without heap allocation.</summary>
    public struct Enumerator : IEnumerator<Behavior>
    {
        private readonly BehaviorCollection collection;
        private int index;

        internal Enumerator(BehaviorCollection collection)
        {
            this.collection = collection;
            index = -1;
        }

        /// <inheritdoc />
        public Behavior Current => collection[index];

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            var next = index + 1;
            if (next >= collection.Count)
            {
                index = collection.Count;
                return false;
            }

            index = next;
            return true;
        }

        /// <inheritdoc />
        public void Reset() => index = -1;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
