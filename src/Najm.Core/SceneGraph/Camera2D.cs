using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Frames a Y-up world onto a Y-down virtual viewport for a two-dimensional world layer.
/// </summary>
/// <remarks>
/// <para>
/// The camera reuses its translation and rotation as its framing values: the camera's world
/// position is the world point that lands at the center of virtual space, and its world rotation
/// turns the view about that point. A camera is an ordinary node, so a camera parented under a rig
/// frames from where the rig puts it — for an unparented camera, its world values are exactly its
/// <see cref="Node2D.Position"/> and <see cref="Node2D.Rotation"/>.
/// <see cref="Node2D.Scale"/> takes no part in framing, and neither does any ancestor's scale; use
/// <see cref="Zoom"/> instead.
/// </para>
/// <para>
/// World space is Y-up and virtual space is Y-down with its origin at the top-left corner, so the
/// mapping flips Y. That flip lives here and nowhere else in the engine.
/// </para>
/// <para>
/// The camera does not own the virtual resolution — the scene does — so every mapping helper takes
/// the virtual size it should frame against.
/// </para>
/// </remarks>
public class Camera2D : Node2D
{
    private float zoom = 1f;

    /// <summary>
    /// Gets or sets the finite, positive number of virtual units drawn per world unit. The default
    /// is one, and a larger value draws world content larger.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and greater than zero.</exception>
    public float Zoom
    {
        get => zoom;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Camera zoom must be finite and greater than zero.");
            }

            zoom = value;
        }
    }

    /// <summary>
    /// Returns the row-vector matrix mapping world coordinates into virtual coordinates.
    /// </summary>
    /// <param name="virtualResolution">The finite, positive virtual viewport size to frame against.</param>
    /// <remarks>
    /// The composition is
    /// <c>Translate(-WorldPosition) * Rotate(-WorldRotation) * Scale(Zoom, -Zoom) * Translate(virtualResolution / 2)</c>,
    /// so <see cref="Node2D.WorldPosition"/> lands exactly on the virtual center and increasing
    /// world Y produces decreasing virtual Y. The translation and rotation are the camera's world
    /// values, so a parented camera rides its rig; the scale factor is <see cref="Zoom"/> alone, so
    /// no ancestor's scale reaches the framing.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The virtual resolution is not finite and positive.</exception>
    /// <exception cref="InvalidOperationException">The mapping is not representable as a finite matrix.</exception>
    public Matrix3x2 WorldToVirtual(in Vector2 virtualResolution)
    {
        EnsureViewport(virtualResolution, nameof(virtualResolution));

        var reducedRotation = Math.IEEERemainder(WorldRotationRadians(), Math.Tau);
        var worldToVirtual =
            Matrix3x2.CreateTranslation(-WorldPosition) *
            Matrix3x2.CreateRotation((float)-reducedRotation) *
            Matrix3x2.CreateScale(zoom, -zoom) *
            Matrix3x2.CreateTranslation(virtualResolution * 0.5f);
        EnsureFiniteMatrix(
            worldToVirtual,
            "The camera's world-to-virtual mapping cannot be represented by a finite Matrix3x2.");

        return worldToVirtual;
    }

    /// <summary>
    /// Returns the row-vector matrix mapping virtual coordinates back into world coordinates.
    /// </summary>
    /// <param name="virtualResolution">The finite, positive virtual viewport size to frame against.</param>
    /// <exception cref="ArgumentOutOfRangeException">The virtual resolution is not finite and positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// The world-to-virtual mapping is singular or its inverse is not representable as a finite matrix.
    /// </exception>
    public Matrix3x2 VirtualToWorld(in Vector2 virtualResolution)
    {
        var worldToVirtual = WorldToVirtual(virtualResolution);
        if (!Matrix3x2.Invert(worldToVirtual, out var virtualToWorld))
        {
            throw new InvalidOperationException(
                "The camera's world-to-virtual mapping is singular and cannot be inverted.");
        }

        EnsureFiniteMatrix(
            virtualToWorld,
            "The camera's virtual-to-world mapping cannot be represented by a finite Matrix3x2.");

        return virtualToWorld;
    }

    /// <summary>Moves the camera so the given world point lands at the center of virtual space.</summary>
    /// <param name="worldPoint">The finite world point to center on.</param>
    /// <remarks>
    /// This changes <see cref="Node2D.Position"/> only; <see cref="Zoom"/> and rotation are
    /// untouched. Framing follows the camera's world position, so a parented camera stores the point
    /// in its parent's local space — an unparented camera stores it exactly as given.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The point is not finite.</exception>
    /// <exception cref="InvalidOperationException">The parent's world transform is singular.</exception>
    public void CenterOn(Vector2 worldPoint) =>
        Position = Parent is Node2D parent
            ? Vector2.Transform(worldPoint, parent.InverseWorld)
            : worldPoint;

    /// <summary>
    /// Centers on the given world rectangle and chooses the largest <see cref="Zoom"/> that keeps
    /// the whole rectangle inside the virtual viewport, preserving aspect.
    /// </summary>
    /// <param name="worldRect">The finite world rectangle to frame, with positive width and height.</param>
    /// <param name="virtualResolution">The finite, positive virtual viewport size to frame against.</param>
    /// <remarks>
    /// This fits rather than fills: the limiting axis touches the viewport edges exactly and the
    /// other axis leaves slack. A non-zero world rotation — the camera's own plus its ancestors' —
    /// is accounted for by fitting the rectangle's rotated extent, so the rectangle stays fully
    /// visible at any rotation. Centering goes through <see cref="CenterOn(Vector2)"/>, so a
    /// parented camera lands on the rectangle's world center rather than beside it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The rectangle is degenerate or not finite, the virtual resolution is not finite and positive,
    /// or the resulting zoom is not finite and greater than zero.
    /// </exception>
    public void FitRect(in Rect worldRect, in Vector2 virtualResolution)
    {
        EnsureViewport(virtualResolution, nameof(virtualResolution));
        if (worldRect.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldRect),
                worldRect,
                "A camera cannot frame a rectangle with zero width or height.");
        }

        var reducedRotation = Math.IEEERemainder(WorldRotationRadians(), Math.Tau);
        var cosine = Math.Abs(Math.Cos(reducedRotation));
        var sine = Math.Abs(Math.Sin(reducedRotation));
        var spanX = (worldRect.Width * cosine) + (worldRect.Height * sine);
        var spanY = (worldRect.Width * sine) + (worldRect.Height * cosine);
        var fitted = Math.Min(virtualResolution.X / spanX, virtualResolution.Y / spanY);

        if (!double.IsFinite(fitted) || fitted <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldRect),
                worldRect,
                "The rectangle cannot be framed by a finite, positive camera zoom.");
        }

        var candidate = (float)fitted;
        if (!float.IsFinite(candidate) || candidate <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldRect),
                worldRect,
                "The rectangle cannot be framed by a finite, positive camera zoom.");
        }

        zoom = candidate;
        CenterOn(new Vector2(
            worldRect.X + (worldRect.Width * 0.5f),
            worldRect.Y + (worldRect.Height * 0.5f)));
    }

    /// <summary>
    /// Returns the camera's world rotation in radians: its own <see cref="Node2D.Rotation"/> plus
    /// every two-dimensional ancestor's, summed up the same chain the world matrix composes over.
    /// </summary>
    /// <remarks>
    /// Summing the angles rather than decomposing <see cref="Node2D.WorldMatrix"/> is deliberate. A
    /// decomposition carries an ancestor's scale into the result — a non-uniform one as skew, a
    /// negative one as a half turn — and framing scale belongs to <see cref="Zoom"/> alone.
    /// </remarks>
    private double WorldRotationRadians()
    {
        var total = 0d;
        for (Node2D? node = this; node is not null; node = node.Parent as Node2D)
        {
            total += node.Rotation.Radians;
        }

        return total;
    }

    private static void EnsureViewport(in Vector2 virtualResolution, string parameterName)
    {
        if (!float.IsFinite(virtualResolution.X) ||
            !float.IsFinite(virtualResolution.Y) ||
            virtualResolution.X <= 0f ||
            virtualResolution.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                virtualResolution,
                "A virtual resolution must have finite, positive components.");
        }
    }

    private static void EnsureFiniteMatrix(in Matrix3x2 matrix, string message)
    {
        if (!float.IsFinite(matrix.M11) ||
            !float.IsFinite(matrix.M12) ||
            !float.IsFinite(matrix.M21) ||
            !float.IsFinite(matrix.M22) ||
            !float.IsFinite(matrix.M31) ||
            !float.IsFinite(matrix.M32))
        {
            throw new InvalidOperationException(message);
        }
    }
}
