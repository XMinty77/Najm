using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>A transformable node in a two-dimensional logical coordinate space.</summary>
public class Node2D : Node
{
    private int zIndex;
    private float opacity = 1f;
    private BlendMode blend = BlendMode.SrcOver;

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
    /// Gets this node's own drawn geometry in local coordinates. The default is empty.
    /// </summary>
    /// <remarks>
    /// The rectangle is local-space and camera-free: it describes geometry before this node's own
    /// transform, before any ancestor transform, and before any camera or scale-pinning
    /// resolution. It covers this node alone and never its children.
    /// </remarks>
    public virtual Rect GeometryBounds => default;

    /// <summary>
    /// Gets the local-coordinate gate used for interaction. The default follows
    /// <see cref="GeometryBounds"/>.
    /// </summary>
    /// <remarks>
    /// The rectangle is local-space and camera-free, exactly as <see cref="GeometryBounds"/> is. It
    /// deliberately ignores visual-only expansion such as glow or blur unless a node opts in by
    /// overriding this property.
    /// </remarks>
    public virtual Rect HitBounds => GeometryBounds;

    /// <summary>
    /// Gets the conservative local-coordinate bound of what this node actually paints. The default
    /// follows <see cref="GeometryBounds"/>.
    /// </summary>
    /// <remarks>
    /// The rectangle is local-space and camera-free, exactly as <see cref="GeometryBounds"/> is.
    /// Strokes, effects, and other visual expansion may push it beyond the drawn geometry, so a
    /// node whose paint reaches outside its geometry must widen this rectangle to match.
    /// </remarks>
    public virtual Rect VisualBounds => GeometryBounds;

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

    internal override int PaintOrderKey => zIndex;

    internal override void OnParentChanged(Node? previousParent, Node? currentParent) =>
        Transform.InvalidateWorldSubtree();
}
