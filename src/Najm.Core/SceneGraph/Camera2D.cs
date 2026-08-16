using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Frames a Y-up world onto a Y-down virtual viewport for a two-dimensional world layer.
/// </summary>
/// <remarks>
/// <para>
/// The camera reuses <see cref="Node2D.Position"/> and <see cref="Node2D.Rotation"/> as its framing
/// values: <see cref="Node2D.Position"/> is the world point that lands at the center of virtual
/// space, and <see cref="Node2D.Rotation"/> turns the view about that point.
/// <see cref="Node2D.Scale"/> takes no part in framing; use <see cref="Zoom"/> instead.
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
    /// <c>Translate(-Position) * Rotate(-Rotation) * Scale(Zoom, -Zoom) * Translate(virtualResolution / 2)</c>,
    /// so <see cref="Node2D.Position"/> lands exactly on the virtual center and increasing world Y
    /// produces decreasing virtual Y.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The virtual resolution is not finite and positive.</exception>
    /// <exception cref="InvalidOperationException">The mapping is not representable as a finite matrix.</exception>
    public Matrix3x2 WorldToVirtual(in Vector2 virtualResolution)
    {
        EnsureViewport(virtualResolution, nameof(virtualResolution));

        var reducedRotation = Math.IEEERemainder(Rotation.Radians, Math.Tau);
        var worldToVirtual =
            Matrix3x2.CreateTranslation(-Position) *
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
    /// <remarks>This changes <see cref="Node2D.Position"/> only; <see cref="Zoom"/> and rotation are untouched.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The point is not finite.</exception>
    public void CenterOn(Vector2 worldPoint) => Position = worldPoint;

    /// <summary>
    /// Centers on the given world rectangle and chooses the largest <see cref="Zoom"/> that keeps
    /// the whole rectangle inside the virtual viewport, preserving aspect.
    /// </summary>
    /// <param name="worldRect">The finite world rectangle to frame, with positive width and height.</param>
    /// <param name="virtualResolution">The finite, positive virtual viewport size to frame against.</param>
    /// <remarks>
    /// This fits rather than fills: the limiting axis touches the viewport edges exactly and the
    /// other axis leaves slack. A non-zero <see cref="Node2D.Rotation"/> is accounted for by fitting
    /// the rectangle's rotated extent, so the rectangle stays fully visible at any rotation.
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

        var reducedRotation = Math.IEEERemainder(Rotation.Radians, Math.Tau);
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
        Position = new Vector2(
            worldRect.X + (worldRect.Width * 0.5f),
            worldRect.Y + (worldRect.Height * 0.5f));
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
