using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>A transformable node in a two-dimensional logical coordinate space.</summary>
public class Node2D : Node
{
    private int zIndex;

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

    internal override int PaintOrderKey => zIndex;

    internal override void OnParentChanged(Node? previousParent, Node? currentParent) =>
        Transform.InvalidateWorldSubtree();
}
