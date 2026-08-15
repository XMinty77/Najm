using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>A transformable node in a two-dimensional logical coordinate space.</summary>
public class Node2D : Node
{
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

    internal override void OnParentChanged(Node? previousParent, Node? currentParent) =>
        Transform.InvalidateWorldSubtree();
}
