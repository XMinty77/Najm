using System.Numerics;

namespace Najm.Core;

/// <summary>Describes an axis-aligned rectangle in backend-neutral local coordinates.</summary>
/// <remarks>
/// Coordinates may be negative. Width and height are nonnegative, and <c>default(Rect)</c> is a
/// valid empty rectangle at the origin.
/// </remarks>
public readonly record struct Rect
{
    /// <summary>Creates a finite rectangle from its top-left location and size.</summary>
    /// <param name="x">The finite left coordinate.</param>
    /// <param name="y">The finite top coordinate.</param>
    /// <param name="width">The finite nonnegative width.</param>
    /// <param name="height">The finite nonnegative height.</param>
    public Rect(float x, float y, float width, float height)
    {
        if (!float.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Rectangle coordinates must be finite.");
        }
        if (!float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Rectangle coordinates must be finite.");
        }
        if (!float.IsFinite(width) || width < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle width must be finite and nonnegative.");
        }
        if (!float.IsFinite(height) || height < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Rectangle height must be finite and nonnegative.");
        }
        if (!float.IsFinite(x + width))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The rectangle's right edge must be finite.");
        }
        if (!float.IsFinite(y + height))
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The rectangle's bottom edge must be finite.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the left coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the top coordinate.</summary>
    public float Y { get; }

    /// <summary>Gets the nonnegative width.</summary>
    public float Width { get; }

    /// <summary>Gets the nonnegative height.</summary>
    public float Height { get; }

    /// <summary>Gets the left edge.</summary>
    public float Left => X;

    /// <summary>Gets the top edge.</summary>
    public float Top => Y;

    /// <summary>Gets the right edge.</summary>
    public float Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Gets whether either dimension is zero.</summary>
    public bool IsEmpty => Width == 0f || Height == 0f;

    /// <summary>Returns whether a point lies inside this rectangle.</summary>
    /// <param name="point">The point to test, in the same space as the rectangle.</param>
    /// <remarks>
    /// <strong>Half-open</strong>, on <c>[Left, Right)</c> by <c>[Top, Bottom)</c>. Two consequences
    /// are the reason: an <see cref="IsEmpty"/> rectangle contains nothing — which matters because
    /// the default <see cref="Najm.Core.Node2D.HitBounds"/> is <c>default(Rect)</c>, and a closed
    /// test would make every plain node a hit at its own origin — and tiling rectangles partition
    /// the plane instead of sharing their seams.
    /// </remarks>
    public bool Contains(Vector2 point) =>
        point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
}
