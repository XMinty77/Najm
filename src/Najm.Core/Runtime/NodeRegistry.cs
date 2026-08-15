namespace Najm.Core;

/// <summary>Tracks attached nodes by identity without imposing a public query order.</summary>
internal sealed class NodeRegistry
{
    private readonly HashSet<Node> nodes = new(ReferenceEqualityComparer.Instance);

    internal int Count => nodes.Count;

    internal bool Contains(Node node) => nodes.Contains(node);

    internal void ValidateAbsentSubtree(Node root)
    {
        if (nodes.Contains(root))
        {
            throw new InvalidOperationException("A node cannot be registered in a scene more than once.");
        }

        for (var index = 0; index < root.ChildCount; index++)
        {
            ValidateAbsentSubtree(root.GetChild(index));
        }
    }

    internal void RegisterSubtree(Node root)
    {
        if (!nodes.Add(root))
        {
            throw new InvalidOperationException("A node cannot be registered in a scene more than once.");
        }

        for (var index = 0; index < root.ChildCount; index++)
        {
            RegisterSubtree(root.GetChild(index));
        }
    }

    internal void UnregisterSubtree(Node root)
    {
        for (var index = 0; index < root.ChildCount; index++)
        {
            UnregisterSubtree(root.GetChild(index));
        }

        if (!nodes.Remove(root))
        {
            throw new InvalidOperationException("The scene node registry is inconsistent during detach.");
        }
    }

    internal void Clear() => nodes.Clear();
}
