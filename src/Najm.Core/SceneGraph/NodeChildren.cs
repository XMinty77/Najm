using System.Collections;

namespace Najm.Core;

/// <summary>Provides a live, read-only, insertion-ordered view of a node's children.</summary>
/// <remarks>
/// Enumerating this concrete view directly uses a value-type enumerator and allocates no managed
/// memory after the view has been created. Enumerating it through an <see cref="IEnumerable"/>
/// interface may box that enumerator; engine traversal uses indexed access.
/// </remarks>
public sealed class NodeChildren : IReadOnlyList<Node>
{
    private readonly Node owner;

    internal NodeChildren(Node owner) => this.owner = owner;

    /// <inheritdoc />
    public Node this[int index] => owner.GetChild(index);

    /// <inheritdoc />
    public int Count => owner.ChildCount;

    /// <summary>Returns an allocation-free enumerator over the current insertion order.</summary>
    public Enumerator GetEnumerator() => new(owner);

    IEnumerator<Node> IEnumerable<Node>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates one concrete <see cref="NodeChildren"/> view without heap allocation.</summary>
    public struct Enumerator : IEnumerator<Node>
    {
        private readonly Node owner;
        private int index;

        internal Enumerator(Node owner)
        {
            this.owner = owner;
            index = -1;
        }

        /// <inheritdoc />
        public Node Current => owner.GetChild(index);

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            var next = index + 1;
            if (next >= owner.ChildCount)
            {
                index = owner.ChildCount;
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
