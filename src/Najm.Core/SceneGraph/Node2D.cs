using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>A transformable node in a two-dimensional logical coordinate space.</summary>
public class Node2D : Node
{
    private int zIndex;
    private float opacity = 1f;
    private BlendMode blend = BlendMode.SrcOver;
    private Rect subtreeGeometryBounds;
    private Rect subtreeHitBounds;
    private Rect subtreeVisualBounds;
    private bool subtreeBoundsDirty = true;

    internal override NodeSpaceKind SpaceKind => NodeSpaceKind.TwoD;

    /// <summary>Creates a node with identity local transform and inherited scale behavior.</summary>
    public Node2D() => Transform = new Transform2D(this);

    /// <summary>Gets this node's logical transform.</summary>
    public Transform2D Transform { get; }

    /// <summary>Gets or sets finite local translation.</summary>
    public Vector2 Position
    {
        get => Transform.Position;
        set => Transform.Position = value;
    }

    /// <summary>Gets or sets finite local rotation.</summary>
    public Angle Rotation
    {
        get => Transform.Rotation;
        set => Transform.Rotation = value;
    }

    /// <summary>Gets or sets finite local scale, including zero and negative components.</summary>
    public Vector2 Scale
    {
        get => Transform.Scale;
        set => Transform.Scale = value;
    }

    /// <summary>Gets or sets camera-resolution scale behavior.</summary>
    /// <remarks>
    /// Only <see cref="Najm.Core.ScaleMode.Inherit"/>, the default, is implemented; see
    /// <see cref="Transform2D.ScaleMode"/> for why requesting scale pinning fails instead of being
    /// accepted and ignored.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not a defined scale mode.</exception>
    /// <exception cref="NotSupportedException">
    /// The value is <see cref="Najm.Core.ScaleMode.Virtual"/>.
    /// </exception>
    public ScaleMode ScaleMode
    {
        get => Transform.ScaleMode;
        set => Transform.ScaleMode = value;
    }

    /// <summary>Gets the cached logical local matrix.</summary>
    public Matrix3x2 LocalMatrix => Transform.LocalMatrix;

    /// <summary>Gets the cached logical world matrix.</summary>
    public Matrix3x2 WorldMatrix => Transform.WorldMatrix;

    /// <summary>Gets the cached inverse logical world matrix.</summary>
    public Matrix3x2 InverseWorld => Transform.InverseWorld;

    /// <summary>Gets the logical world-space position of this node's origin.</summary>
    public Vector2 WorldPosition => Transform.WorldPosition;

    /// <summary>Returns the matrix that maps this node's local space into another node's local space.</summary>
    public Matrix3x2 TransformTo(Node2D other) => Transform.TransformTo(other);

    /// <summary>Maps a finite point from this node's local space into another node's local space.</summary>
    public Vector2 ToLocalOf(Node2D other, Vector2 point) => Transform.ToLocalOf(other, point);

    /// <summary>
    /// Gets or sets this node's sibling paint-order key. Siblings paint in a stable sort by
    /// ascending value, and equal values keep their insertion order.
    /// </summary>
    /// <remarks>
    /// This key affects paint order only. Update order, behavior order, and layout order stay in
    /// insertion order no matter how this value is set.
    /// </remarks>
    public int ZIndex
    {
        get => zIndex;
        set
        {
            if (zIndex == value)
            {
                return;
            }

            zIndex = value;
            Parent?.InvalidatePaintOrder();
        }
    }

    /// <summary>
    /// Gets this node's own drawn geometry in local coordinates, before effects. The default is
    /// empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rectangle is local-space and camera-free: it describes geometry before this node's own
    /// transform, before any ancestor transform, and before any camera or scale-pinning
    /// resolution.
    /// </para>
    /// <para>
    /// This is the node's <em>own</em> contribution and never its children's, because that is the
    /// only thing an override can honestly state: a subclass knows what it draws and cannot know
    /// what will be parented beneath it. §6.6's node-and-descendants value is the aggregate,
    /// <see cref="SubtreeGeometryBounds"/>, which the engine composes from these declarations and
    /// which no subclass overrides.
    /// </para>
    /// <para>
    /// An override whose value can change must call <see cref="InvalidateBounds"/> when it does, or
    /// the aggregates above it keep the value they last read.
    /// </para>
    /// </remarks>
    public virtual Rect GeometryBounds => default;

    /// <summary>
    /// Gets the local-coordinate gate used for interaction by this node alone. The default follows
    /// <see cref="GeometryBounds"/>.
    /// </summary>
    /// <remarks>
    /// The rectangle is local-space and camera-free, exactly as <see cref="GeometryBounds"/> is,
    /// and covers this node's own gate rather than its subtree's — see
    /// <see cref="SubtreeHitBounds"/> for the aggregate. It deliberately ignores visual-only
    /// expansion such as glow or blur unless a node opts in by overriding this property. An
    /// override whose value can change must call <see cref="InvalidateBounds"/> when it does.
    /// </remarks>
    public virtual Rect HitBounds => GeometryBounds;

    /// <summary>
    /// Gets the conservative local-coordinate bound of what this node itself paints. The default
    /// follows <see cref="GeometryBounds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rectangle is local-space and camera-free, exactly as <see cref="GeometryBounds"/> is.
    /// Strokes, effects, and other visual expansion may push it beyond the drawn geometry, so a
    /// node whose paint reaches outside its geometry must widen this rectangle to match.
    /// </para>
    /// <para>
    /// <strong>This is the node's own paint, not its unit's.</strong> §6.6 describes visual bounds
    /// as "the conservative visible output of the node and descendants", and that value is
    /// <see cref="SubtreeVisualBounds"/> — the aggregate the same paragraph requires each of the
    /// three to have. Sizing an isolation bracket (§6.7) from this property instead of from the
    /// aggregate would clip descendant paint, because a bracket spans the node's whole compositing
    /// unit. An override whose value can change must call <see cref="InvalidateBounds"/> when it
    /// does.
    /// </para>
    /// </remarks>
    public virtual Rect VisualBounds => GeometryBounds;

    /// <summary>
    /// Gets §6.6's subtree aggregate of <see cref="GeometryBounds"/>: this node's own geometry
    /// unioned with every descendant's, mapped into this node's local space.
    /// </summary>
    /// <inheritdoc cref="SubtreeVisualBounds" path="/remarks" />
    public Rect SubtreeGeometryBounds
    {
        get
        {
            EnsureSubtreeBounds();
            return subtreeGeometryBounds;
        }
    }

    /// <summary>
    /// Gets §6.6's subtree aggregate of <see cref="HitBounds"/>: this node's own interaction gate
    /// unioned with every descendant's, mapped into this node's local space.
    /// </summary>
    /// <inheritdoc cref="SubtreeVisualBounds" path="/remarks" />
    public Rect SubtreeHitBounds
    {
        get
        {
            EnsureSubtreeBounds();
            return subtreeHitBounds;
        }
    }

    /// <summary>
    /// Gets §6.6's subtree aggregate of <see cref="VisualBounds"/>: this node's own paint unioned
    /// with every descendant's, mapped into this node's local space. This is the
    /// node-and-descendants value §6.7 sizes an isolation bracket from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Space.</strong> The result is in <em>this</em> node's local coordinates, on the same
    /// camera-free footing as the per-node trio: a child's aggregate is composed through the
    /// child's own <see cref="LocalMatrix"/> before it is unioned in, and a rotated child
    /// contributes the axis-aligned hull of its rotated rectangle, which is conservative rather
    /// than tight. This node's own transform is deliberately <em>not</em> applied — an aggregate
    /// stated in the parent's space would have to be recomputed every time the node moved, and the
    /// caller that needs device pixels composes the resolved transform anyway.
    /// </para>
    /// <para>
    /// <strong>Empty.</strong> A rectangle with zero extent on either axis covers nothing and
    /// contributes nothing to the union, so <c>default(Rect)</c> — the default of all three
    /// declarations — does not drag the local origin into an ancestor's aggregate. An empty subtree
    /// is therefore exactly <c>default(Rect)</c>, whether it is empty because the node has no
    /// children or because nothing in it declares an extent. A child with empty bounds is not the
    /// same as an absent child even though both add nothing directly: the present child's own
    /// descendants are still walked and still contribute, and it starts contributing the moment its
    /// declaration stops being empty.
    /// </para>
    /// <para>
    /// <strong>What is not folded in.</strong> <see cref="Node.Visible"/> is ignored, so hiding a
    /// node does not shrink the aggregates above it. Bounds are the subtree's static extent, a
    /// visibility flag flips per frame, and a conservative rectangle is safe where a stale one is
    /// not. <see cref="Clip"/> is not intersected either: §6.7 has the traverser intersect resolved
    /// visual bounds with the <em>active</em> clip when it sizes a bracket, and pre-applying a
    /// node's own clip here would double-count that.
    /// </para>
    /// <para>
    /// <strong>Cost.</strong> The three aggregates are computed together in one walk and cached
    /// behind a single dirty flag, following <see cref="Transform2D"/>'s idiom. Invalidation runs
    /// <em>upward</em> — §6.6's word — and stops at the first ancestor already marked, so a warm
    /// read of a clean tree is a field read and a mutation costs one short walk to the root. The
    /// walk allocates nothing. Adding a child, removing one, and moving one invalidate
    /// automatically; a subclass whose declared bounds change must say so with
    /// <see cref="InvalidateBounds"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A descendant's bounds cannot be mapped into this node's local space as a finite rectangle.
    /// </exception>
    public Rect SubtreeVisualBounds
    {
        get
        {
            EnsureSubtreeBounds();
            return subtreeVisualBounds;
        }
    }

    /// <summary>
    /// Gets or sets the group alpha of this node's compositing unit — itself and its whole subtree
    /// — from zero to one inclusive. The default is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <em>group</em> opacity, not a per-primitive alpha multiply. The node and everything
    /// beneath it composite once, and the result of that composite is attenuated. Two overlapping
    /// children under a parent at <c>0.5</c> therefore show the half-alpha composite of their union;
    /// halving each child independently would darken the overlap instead, and the two readings
    /// differ visibly wherever content overlaps.
    /// </para>
    /// <para>
    /// A value below one makes the unit isolate, which costs an offscreen group for the subtree —
    /// see §1.4's warning that composition brackets are fill-rate cost. The default of one is free:
    /// no bracket is opened and nothing is allocated.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and within zero to one.</exception>
    public float Opacity
    {
        get => opacity;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Node opacity must be finite and between zero and one inclusive.");
            }

            opacity = value;
        }
    }

    /// <summary>
    /// Gets or sets the blend this node's compositing unit uses against its current isolation
    /// scope. The default is <see cref="BlendMode.SrcOver"/>.
    /// </summary>
    /// <remarks>
    /// The unit blends against the scope that encloses it — the innermost isolating ancestor, or
    /// failing that the node's own layer — never against the assembled frame. A non-default value
    /// makes the unit isolate.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not a defined blend mode.</exception>
    public BlendMode Blend
    {
        get => blend;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The blend mode is not defined.", nameof(value));
            }

            blend = value;
        }
    }

    /// <summary>
    /// Gets or sets the rectangle, in this node's local coordinates, that bounds what its
    /// compositing unit — itself and its whole subtree — is allowed to paint. The default is
    /// <see langword="null"/>, which clips nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clip brackets the same span <see cref="Opacity"/>'s group does: it is opened before this
    /// node paints and closed after the last descendant, so it bounds the subtree rather than the
    /// node. That is the difference between saying it here and pushing
    /// <see cref="IDrawContext2D.PushClip(in Rect)"/> inside each leaf's <c>Render</c> — a leaf can
    /// only clip itself, and a leaf that forgets is not clipped at all. A descendant cannot escape
    /// this rectangle by pushing state of its own; author clips compose strictly inside it.
    /// </para>
    /// <para>
    /// The rectangle is in <em>this</em> node's local space, so it moves, rotates, and scales with
    /// the node. Under a rotation it stays the rotated rectangle rather than collapsing to its
    /// axis-aligned device bound. <c>new Rect()</c> is a valid empty rectangle and hides the whole
    /// subtree, which is a legitimate thing to say; use <see cref="Node.Visible"/> when that is what
    /// you mean, because an invisible node costs the walk nothing while a clipped one still walks.
    /// </para>
    /// <para>
    /// <strong>A clip bounds without isolating.</strong> Per §6.7's table, clip state alone does not
    /// isolate: a clipped node is not thereby a stacking scope, so a descendant carrying a
    /// non-default <see cref="Blend"/> still composites against whatever lies beneath this node
    /// rather than against the clipped subtree. Use <see cref="Isolate"/> when a scope is what you
    /// want. The realization matches the semantics — a saved clip, never an offscreen group — so a
    /// clip costs a clip-stack entry rather than the fill rate §1.4 warns composition brackets cost.
    /// A node that clips <em>and</em> isolates gets both, with the clip bounding what the group
    /// captures.
    /// </para>
    /// <para>
    /// Only a rectangle is expressible; path clips and <c>Mask</c> are M2 and are not approximated.
    /// </para>
    /// </remarks>
    public Rect? Clip { get; set; }

    /// <summary>
    /// Gets or sets whether this node forces an explicit compositing scope even when nothing else
    /// requires one. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Setting it is how an author makes a subtree a stacking scope on purpose: descendants with a
    /// non-default <see cref="Blend"/> then composite against this unit rather than against
    /// whatever lies outside it.
    /// </remarks>
    public bool Isolate { get; set; }

    /// <summary>
    /// Gets whether this node's composition state requires its subtree to be bracketed as one
    /// isolated unit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §6.7's isolation predicate, restricted to the M1 property set: a unit isolates when its
    /// opacity is below one, its blend is not the default, or <see cref="Isolate"/> is set. The full
    /// predicate also isolates on a mask, an effect, or a backdrop, and those terms arrive with the
    /// properties themselves rather than as unreachable code here.
    /// </para>
    /// <para>
    /// <strong><see cref="Clip"/> is deliberately absent</strong>, because §6.7's table says clip
    /// state alone does not isolate. A clip bounds the subtree through a bracket of its own — a
    /// saved clip, no offscreen — so a clipped node is not a stacking scope and a descendant's
    /// non-default <see cref="Blend"/> composites past it. Adding it here would be both a semantic
    /// change and an offscreen nobody asked for.
    /// </para>
    /// <para>
    /// This is the render walk's per-node fast path, so it is three field reads and no branching
    /// into anything that allocates. The overwhelmingly common node — default opacity, default
    /// blend, no forced isolation — answers <see langword="false"/> and costs the walk nothing
    /// beyond the test itself. §6.7's <c>CompositionAtomicity.SinglePrimitive</c> exemption, which
    /// would let a verified single-primitive node fold its opacity into its own paint instead of
    /// bracketing, is deliberately absent: without the verification it would silently give custom
    /// drawables per-primitive opacity where they were promised group opacity.
    /// </para>
    /// </remarks>
    internal bool RequiresIsolation =>
        opacity < 1f || blend != BlendMode.SrcOver || Isolate;

    /// <summary>
    /// Declares that this node's own <see cref="GeometryBounds"/>, <see cref="HitBounds"/>, or
    /// <see cref="VisualBounds"/> has changed, so the subtree aggregates of this node and its
    /// ancestors are recomputed on their next read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §6.6 has bounds invalidate upward on "geometry, transform, style, effect, or child changes".
    /// Two of those the engine can see for itself — a transform change and a child arriving or
    /// leaving invalidate on their own. Geometry, style, and effect it cannot: the three
    /// declarations are virtual, and a subclass computes them from state only the subclass knows.
    /// This is that signal, and a subclass whose declared bounds vary is the only thing that can
    /// send it.
    /// </para>
    /// <para>
    /// Calling it when nothing changed is safe and costs one walk to the root; not calling it when
    /// something did leaves every ancestor aggregate holding the value it last read. The per-node
    /// declarations themselves are never cached, so they stay correct either way.
    /// </para>
    /// </remarks>
    protected void InvalidateBounds() => InvalidateSubtreeBounds();

    internal override int PaintOrderKey => zIndex;

    internal override void OnParentChanged(Node? previousParent, Node? currentParent)
    {
        Transform.InvalidateWorldSubtree();

        // Both ends move: the subtree left one aggregate and joined another. A reparent arrives
        // here as a removal and then an addition, so both calls are made for it too.
        (previousParent as Node2D)?.InvalidateSubtreeBounds();
        (currentParent as Node2D)?.InvalidateSubtreeBounds();
    }

    /// <summary>
    /// Marks this node's subtree aggregates dirty and walks the same mark to the root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk stops at the first ancestor already marked, which is what keeps a burst of
    /// mutations near a leaf from costing a full walk each. That early exit is sound because the
    /// class maintains the invariant <em>a dirty node's ancestors are all dirty</em>: it holds at
    /// construction, where every node starts dirty; this walk preserves it by marking a contiguous
    /// run up to an already-dirty ancestor, whose own ancestors are dirty by the invariant; and
    /// <see cref="EnsureSubtreeBounds"/> preserves it by clearing only a node and its descendants,
    /// which never leaves a dirty node with a clean ancestor.
    /// </para>
    /// <para>
    /// A node's own aggregates do not depend on its own local matrix — a child is composed through
    /// the <em>child's</em> matrix — so a transform change invalidates from the parent, not from
    /// the node that moved.
    /// </para>
    /// </remarks>
    internal void InvalidateSubtreeBounds()
    {
        for (Node2D? node = this; node is not null; node = node.Parent as Node2D)
        {
            if (node.subtreeBoundsDirty)
            {
                return;
            }

            node.subtreeBoundsDirty = true;
        }
    }

    /// <summary>
    /// Recomputes all three subtree aggregates in one depth-first walk, if this node is dirty.
    /// </summary>
    /// <remarks>
    /// One walk rather than three: a child answers all three of its own aggregates from one
    /// recursive call and one matrix, so the shared cost — the recursion and the composition — is
    /// paid once. Nothing here allocates.
    /// </remarks>
    private void EnsureSubtreeBounds()
    {
        if (!subtreeBoundsDirty)
        {
            return;
        }

        var geometry = GeometryBounds;
        var hit = HitBounds;
        var visual = VisualBounds;

        for (var index = 0; index < ChildCount; index++)
        {
            if (GetChild(index) is not Node2D child)
            {
                continue;
            }

            child.EnsureSubtreeBounds();
            var childToLocal = child.LocalMatrix;
            geometry = Combine(geometry, Project(child.subtreeGeometryBounds, childToLocal));
            hit = Combine(hit, Project(child.subtreeHitBounds, childToLocal));
            visual = Combine(visual, Project(child.subtreeVisualBounds, childToLocal));
        }

        subtreeGeometryBounds = Normalize(geometry);
        subtreeHitBounds = Normalize(hit);
        subtreeVisualBounds = Normalize(visual);
        subtreeBoundsDirty = false;
    }

    /// <summary>
    /// Collapses any rectangle covering no area to <c>default(Rect)</c>, so an empty aggregate
    /// carries no position an ancestor could mistake for one.
    /// </summary>
    private static Rect Normalize(in Rect value) => value.IsEmpty ? default : value;

    /// <summary>Returns the union of two rectangles, treating an empty one as no contribution.</summary>
    private static Rect Combine(in Rect accumulated, in Rect candidate)
    {
        if (candidate.IsEmpty)
        {
            return accumulated;
        }
        if (accumulated.IsEmpty)
        {
            return candidate;
        }

        var left = MathF.Min(accumulated.Left, candidate.Left);
        var top = MathF.Min(accumulated.Top, candidate.Top);
        var right = MathF.Max(accumulated.Right, candidate.Right);
        var bottom = MathF.Max(accumulated.Bottom, candidate.Bottom);
        return Build(left, top, right, bottom);
    }

    /// <summary>
    /// Maps one rectangle through a matrix and returns the axis-aligned hull of the result.
    /// </summary>
    /// <remarks>
    /// All four corners are mapped, not two, because a rotation or a negative scale sends the
    /// top-left corner somewhere that is no longer the minimum. The hull is conservative under
    /// rotation by construction, which is what §6.6 asks a visual bound to be.
    /// </remarks>
    private static Rect Project(in Rect local, in Matrix3x2 childToLocal)
    {
        if (local.IsEmpty)
        {
            return default;
        }

        var topLeft = Vector2.Transform(new Vector2(local.Left, local.Top), childToLocal);
        var topRight = Vector2.Transform(new Vector2(local.Right, local.Top), childToLocal);
        var bottomRight = Vector2.Transform(new Vector2(local.Right, local.Bottom), childToLocal);
        var bottomLeft = Vector2.Transform(new Vector2(local.Left, local.Bottom), childToLocal);

        return Build(
            MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X)),
            MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y)),
            MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X)),
            MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y)));
    }

    /// <summary>Builds a rectangle from its edges, rejecting a span no rectangle can hold.</summary>
    /// <remarks>
    /// The edges are finite whenever their inputs are, but their difference need not be: two edges
    /// near opposite ends of the float range span more than a float can express. That is reported
    /// rather than silently truncated to infinity, which <see cref="Rect"/> would refuse anyway
    /// with a message about a constructor argument nobody passed.
    /// </remarks>
    private static Rect Build(float left, float top, float right, float bottom)
    {
        var width = right - left;
        var height = bottom - top;
        if (!float.IsFinite(left) || !float.IsFinite(top) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            throw new InvalidOperationException(
                "A subtree bounds aggregate is not representable as a finite rectangle.");
        }

        return new Rect(left, top, width, height);
    }
}
