using System.Collections;

namespace Najm.Core;

/// <summary>Provides a controlled, add-ordered collection of scene layers.</summary>
public sealed class LayerStack : IReadOnlyList<Layer>
{
    private readonly Scene owner;
    private readonly List<Layer> items = [];

    internal LayerStack(Scene owner) => this.owner = owner;

    /// <inheritdoc />
    public Layer this[int index] => items[index];

    /// <inheritdoc />
    public int Count => items.Count;

    /// <summary>Adds an unowned layer and returns it with its concrete type preserved.</summary>
    public T Add<T>(T layer)
        where T : Layer
    {
        ArgumentNullException.ThrowIfNull(layer);
        owner.RequestAddLayer(layer);
        return layer;
    }

    /// <summary>Removes a layer by identity.</summary>
    public bool Remove(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return owner.RequestRemoveLayer(layer);
    }

    /// <summary>Returns an allocation-free enumerator over add order.</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<Layer> IEnumerable<Layer>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void AddImmediate(Layer layer)
    {
        if (layer.OwnerStack is not null)
        {
            var message = ReferenceEquals(layer.OwnerStack, this)
                ? "The layer is already in this scene's layer stack."
                : "The layer is already owned by a different scene.";
            throw new InvalidOperationException(message);
        }
        if (layer.ReservationStack is not null && !ReferenceEquals(layer.ReservationStack, this))
        {
            throw new InvalidOperationException("A layer reserved by a scene mutation cannot be claimed elsewhere.");
        }

        items.Add(layer);
        layer.SetOwnerStack(this);
    }

    internal bool RemoveImmediate(Layer layer)
    {
        if (!ReferenceEquals(layer.OwnerStack, this))
        {
            return false;
        }

        var index = IndexOf(layer);
        if (index < 0)
        {
            throw new InvalidOperationException("The layer owner and stack are inconsistent.");
        }

        items.RemoveAt(index);
        layer.SetOwnerStack(null);
        return true;
    }

    internal Layer[] Snapshot() => [.. items];

    internal void Restore(Layer[] snapshot)
    {
        var foreignClaimCount = 0;
        foreach (var layer in snapshot)
        {
            if (layer.OwnerStack is not null && !ReferenceEquals(layer.OwnerStack, this))
            {
                foreignClaimCount++;
            }
        }

        foreach (var layer in items)
        {
            if (ReferenceEquals(layer.OwnerStack, this))
            {
                layer.SetOwnerStack(null);
            }
        }

        items.Clear();
        foreach (var layer in snapshot)
        {
            if (layer.OwnerStack is not null && !ReferenceEquals(layer.OwnerStack, this))
            {
                continue;
            }

            items.Add(layer);
            layer.SetOwnerStack(this);
        }

        if (foreignClaimCount != 0)
        {
            throw new InvalidOperationException(
                $"Failed to restore {foreignClaimCount} layer snapshot entr{(foreignClaimCount == 1 ? "y" : "ies")} " +
                "because another scene claimed the layer during Load.");
        }
    }

    private int IndexOf(Layer layer)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], layer))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Enumerates one concrete layer stack without heap allocation.</summary>
    public struct Enumerator : IEnumerator<Layer>
    {
        private readonly LayerStack stack;
        private int index;

        internal Enumerator(LayerStack stack)
        {
            this.stack = stack;
            index = -1;
        }

        /// <inheritdoc />
        public Layer Current => stack[index];

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            var next = index + 1;
            if (next >= stack.Count)
            {
                index = stack.Count;
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
