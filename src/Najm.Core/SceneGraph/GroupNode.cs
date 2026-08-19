namespace Najm.Core;

/// <summary>A <see cref="Node2D"/> that paints nothing, used to give a subtree a named parent.</summary>
/// <remarks>
/// <para>
/// <strong>This type has no behavior of its own.</strong> It adds no members, overrides nothing, and
/// costs nothing that a plain <see cref="Node2D"/> does not: <c>new GroupNode()</c> and
/// <c>new Node2D()</c> produce nodes the render walk, the update walk, and the composition model
/// cannot tell apart. It exists so that a grouping node <em>reads</em> as one at the call site, and
/// so that ARCHITECTURE Appendix B.3's <c>layer.Root.Add(new GroupNode())</c> compiles as written.
/// </para>
/// <para>
/// Use it where a node's whole job is to carry a transform, a <see cref="Node2D.ZIndex"/>, a
/// <see cref="Node2D.Clip"/>, or a <see cref="Node2D.Opacity"/> for the subtree beneath it. Do not
/// reach for it expecting it to bracket, batch, or flatten anything — grouping in Najm is a
/// property of the composition state a node carries, never of the node's type. A
/// <see cref="Node2D"/> in the same place is equally correct and equally fast, and existing code
/// that uses one has nothing to migrate.
/// </para>
/// </remarks>
public class GroupNode : Node2D
{
}
