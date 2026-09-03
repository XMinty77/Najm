using System.Numerics;

namespace Najm.Core;

/// <summary>Holds one node's local↔virtual mapping and its resolved bounds, for the layer and camera that produced it.</summary>
/// <remarks>
/// <para>
/// This is the value ARCHITECTURE §9.2 hands the router: "local↔virtual transforms and resolved
/// hit/visual bounds for the current layer, camera, viewport, and scale mode". It exists because a
/// node's own <see cref="Node2D.WorldMatrix"/> is not enough to answer a question about a point on
/// screen. §6.3's camera-dependence rule is explicit that pinning is <em>never</em> baked into
/// <c>WorldMatrix</c> — that would break rendering one ticked frame through two cameras — so the
/// mapping from local space to virtual space only exists once a camera is in hand. That is what
/// this type is: the answer, computed against a specific camera at a specific moment.
/// </para>
/// <para>
/// <strong>Virtual, not device.</strong> The mapping stops at virtual coordinates (§3.3) rather than
/// device pixels, because that is where input lives and where a <c>ScreenLayer</c> node's own
/// coordinates already are. Render scale sits below this and does not enter.
/// </para>
/// <para>
/// <strong>Bounds are conservative hulls.</strong> A rotated node's local rectangle maps to a
/// rotated quadrilateral; the values here are its axis-aligned hull, which is what a cheap gate
/// wants. Passing that gate is not a hit — §9.2 follows it with
/// <see cref="Node2D.HitTest(Vector2)"/> in local space, which is where an exact answer belongs.
/// </para>
/// <para>
/// A frame is a snapshot. Move the node or the camera and it is stale; ask the layer again.
/// </para>
/// </remarks>
public readonly struct ResolvedNodeFrame
{
    private readonly Matrix3x2 virtualToLocal;

    internal ResolvedNodeFrame(in Matrix3x2 localToVirtual, in Rect hitLocal, in Rect visualLocal)
    {
        LocalToVirtualMatrix = localToVirtual;
        IsMappable = Matrix3x2.Invert(localToVirtual, out virtualToLocal);
        HitBoundsVirtual = MapBounds(hitLocal, localToVirtual);
        VisualBoundsVirtual = MapBounds(visualLocal, localToVirtual);
    }

    /// <summary>Gets the row-vector matrix mapping this node's local coordinates into virtual space.</summary>
    public Matrix3x2 LocalToVirtualMatrix { get; }

    /// <summary>
    /// Gets the inverse mapping, from virtual space into this node's local coordinates. Identity
    /// when <see cref="IsMappable"/> is false.
    /// </summary>
    public Matrix3x2 VirtualToLocalMatrix => IsMappable ? virtualToLocal : Matrix3x2.Identity;

    /// <summary>
    /// Gets whether the mapping can be inverted, and therefore whether a virtual point can be
    /// carried back into this node's local space.
    /// </summary>
    /// <remarks>
    /// False for a node collapsed by a zero scale somewhere in its chain. Such a node draws nothing
    /// and occupies no area, so its resolved bounds are empty and the router's gate rejects it
    /// before this ever matters.
    /// </remarks>
    public bool IsMappable { get; }

    /// <summary>Gets the axis-aligned virtual-space hull of the node's <see cref="Node2D.HitBounds"/>.</summary>
    /// <remarks>This is the rectangle §6.6 requires input gating to use.</remarks>
    public Rect HitBoundsVirtual { get; }

    /// <summary>Gets the axis-aligned virtual-space hull of the node's <see cref="Node2D.VisualBounds"/>.</summary>
    public Rect VisualBoundsVirtual { get; }

    /// <summary>Maps a virtual-space point into the node's local coordinates.</summary>
    /// <param name="pointVirtual">The point in virtual space (§3.3).</param>
    /// <exception cref="InvalidOperationException">The mapping is not invertible.</exception>
    public Vector2 VirtualToLocal(Vector2 pointVirtual) =>
        IsMappable
            ? Vector2.Transform(pointVirtual, virtualToLocal)
            : throw new InvalidOperationException(
                "This node's local-to-virtual mapping is singular — a zero scale collapses it — so " +
                "a virtual point has no local preimage. Check IsMappable, or the resolved bounds, " +
                "which are empty for exactly these nodes.");

    /// <summary>Maps a point in the node's local coordinates into virtual space.</summary>
    /// <param name="pointLocal">The point in the node's local space.</param>
    public Vector2 LocalToVirtual(Vector2 pointLocal) =>
        Vector2.Transform(pointLocal, LocalToVirtualMatrix);

    private static Rect MapBounds(in Rect local, in Matrix3x2 mapping)
    {
        if (local.Width == 0f && local.Height == 0f && local.X == 0f && local.Y == 0f)
        {
            return default;
        }

        var a = Vector2.Transform(new Vector2(local.Left, local.Top), mapping);
        var b = Vector2.Transform(new Vector2(local.Right, local.Top), mapping);
        var c = Vector2.Transform(new Vector2(local.Right, local.Bottom), mapping);
        var d = Vector2.Transform(new Vector2(local.Left, local.Bottom), mapping);

        var minX = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
        var minY = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
        var maxX = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
        var maxY = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));

        return float.IsFinite(minX) && float.IsFinite(minY) &&
               float.IsFinite(maxX - minX) && float.IsFinite(maxY - minY)
            ? new Rect(minX, minY, maxX - minX, maxY - minY)
            : default;
    }
}
