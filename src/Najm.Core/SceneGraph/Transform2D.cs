using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Owns one <see cref="Node2D"/>'s logical local and world transform.</summary>
/// <remarks>
/// Najm uses row vectors. Local matrices are composed as
/// Scale × Rotation × Translation, and world matrices as Local × Parent.World.
/// <see cref="ScaleMode"/> is intentionally absent from these logical matrices.
/// </remarks>
public sealed class Transform2D
{
    private readonly Node2D owner;
    private Vector2 position;
    private Angle rotation;
    private Vector2 scale = Vector2.One;
    private ScaleMode scaleMode;
    private Matrix3x2 localMatrix;
    private Matrix3x2 worldMatrix;
    private Matrix3x2 inverseWorld;
    private bool localDirty = true;
    private bool worldDirty = true;
    private bool inverseDirty = true;

    internal Transform2D(Node2D owner) => this.owner = owner;

    /// <summary>Gets or sets finite local translation.</summary>
    public Vector2 Position
    {
        get => position;
        set
        {
            EnsureFinite(value, nameof(value), "Position");
            if (position == value)
            {
                return;
            }

            position = value;
            InvalidateLocal();
        }
    }

    /// <summary>
    /// Gets or sets finite local rotation. The stored angle is not normalized;
    /// matrix construction reduces it by complete turns before converting to float.
    /// </summary>
    public Angle Rotation
    {
        get => rotation;
        set
        {
            if (rotation == value)
            {
                return;
            }

            rotation = value;
            InvalidateLocal();
        }
    }

    /// <summary>
    /// Gets or sets finite local scale. Zero components are allowed; querying
    /// <see cref="InverseWorld"/> then fails while the world matrix is singular.
    /// </summary>
    public Vector2 Scale
    {
        get => scale;
        set
        {
            EnsureFinite(value, nameof(value), "Scale");
            if (scale == value)
            {
                return;
            }

            scale = value;
            InvalidateLocal();
        }
    }

    /// <summary>
    /// Gets or sets camera-resolution scale behavior without changing logical matrices.
    /// </summary>
    /// <remarks>
    /// Only <see cref="Najm.Core.ScaleMode.Inherit"/>, the default, is implemented. Scale pinning
    /// resolves per node against the layer's camera inside the render traverser, and that work has
    /// not landed, so requesting <see cref="Najm.Core.ScaleMode.Virtual"/> is refused here rather
    /// than accepted and then rendered as <see cref="Najm.Core.ScaleMode.Inherit"/> — an author's
    /// unsupported request fails loudly instead of silently changing what is drawn.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not a defined scale mode.</exception>
    /// <exception cref="NotSupportedException">
    /// The value is <see cref="Najm.Core.ScaleMode.Virtual"/>.
    /// </exception>
    public ScaleMode ScaleMode
    {
        get => scaleMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The scale mode is not defined.", nameof(value));
            }

            if (value == Najm.Core.ScaleMode.Virtual)
            {
                throw new NotSupportedException(
                    "Scale pinning (ScaleMode.Virtual, ARCHITECTURE section 6.3) is not implemented: " +
                    "it lands with the pinning resolution in the render traverser. Until then only " +
                    "ScaleMode.Inherit is supported, and this request is refused rather than " +
                    "silently rendered as ScaleMode.Inherit.");
            }

            scaleMode = value;
        }
    }

    /// <summary>Gets the cached row-vector local matrix.</summary>
    public Matrix3x2 LocalMatrix
    {
        get
        {
            if (localDirty)
            {
                var reducedRotation = Math.IEEERemainder(rotation.Radians, Math.Tau);
                var computedLocal =
                    Matrix3x2.CreateScale(scale) *
                    Matrix3x2.CreateRotation((float)reducedRotation) *
                    Matrix3x2.CreateTranslation(position);
                EnsureFiniteMatrix(
                    computedLocal,
                    "The local transform cannot be represented by a finite Matrix3x2.");

                localMatrix = computedLocal;
                localDirty = false;
            }

            return localMatrix;
        }
    }

    /// <summary>Gets the cached logical world matrix.</summary>
    public Matrix3x2 WorldMatrix
    {
        get
        {
            if (worldDirty)
            {
                var computedWorld = owner.Parent is Node2D parent
                    ? LocalMatrix * parent.WorldMatrix
                    : LocalMatrix;
                EnsureFiniteMatrix(
                    computedWorld,
                    "The composed world transform cannot be represented by a finite Matrix3x2.");

                worldMatrix = computedWorld;
                worldDirty = false;
                inverseDirty = true;
            }

            return worldMatrix;
        }
    }

    /// <summary>Gets the cached inverse logical world matrix.</summary>
    /// <exception cref="InvalidOperationException">The logical world transform is singular.</exception>
    public Matrix3x2 InverseWorld
    {
        get
        {
            if (inverseDirty)
            {
                var computedInverse = InvertFinite(WorldMatrix);

                inverseWorld = computedInverse;
                inverseDirty = false;
            }

            return inverseWorld;
        }
    }

    /// <summary>Gets the logical world-space position of the local origin.</summary>
    public Vector2 WorldPosition
    {
        get
        {
            var world = WorldMatrix;
            return new Vector2(world.M31, world.M32);
        }
    }

    /// <summary>Returns the matrix that maps this node's local space into another node's local space.</summary>
    public Matrix3x2 TransformTo(Node2D other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return WorldMatrix * other.InverseWorld;
    }

    /// <summary>Maps a finite point from this node's local space into another node's local space.</summary>
    public Vector2 ToLocalOf(Node2D other, Vector2 point)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureFinite(point, nameof(point), "Point");
        return Vector2.Transform(point, TransformTo(other));
    }

    internal void InvalidateWorldSubtree()
    {
        if (worldDirty)
        {
            return;
        }

        worldDirty = true;
        inverseDirty = true;

        for (var index = 0; index < owner.ChildCount; index++)
        {
            if (owner.GetChild(index) is Node2D child)
            {
                child.Transform.InvalidateWorldSubtree();
            }
        }
    }

    private void InvalidateLocal()
    {
        localDirty = true;
        InvalidateWorldSubtree();

        // A node's own subtree bounds are stated in its own local space and so do not move when it
        // does; what moves is this subtree's contribution to the parent's aggregate. Invalidating
        // from the parent rather than from the owner is therefore both correct and one recompute
        // cheaper (Node2D.SubtreeVisualBounds).
        (owner.Parent as Node2D)?.InvalidateSubtreeBounds();
    }

    private static void EnsureFinite(Vector2 value, string parameterName, string label)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{label} components must be finite.");
        }
    }

    private static Matrix3x2 InvertFinite(Matrix3x2 matrix)
    {
        var m11 = (double)matrix.M11;
        var m12 = (double)matrix.M12;
        var m21 = (double)matrix.M21;
        var m22 = (double)matrix.M22;
        var m31 = (double)matrix.M31;
        var m32 = (double)matrix.M32;
        var determinant = (m11 * m22) - (m21 * m12);

        if (determinant == 0d || !double.IsFinite(determinant))
        {
            throw new InvalidOperationException(
                "The node's world transform is singular and cannot be inverted.");
        }

        var inverse = new Matrix3x2(
            ToRepresentableFloat(m22 / determinant),
            ToRepresentableFloat(-m12 / determinant),
            ToRepresentableFloat(-m21 / determinant),
            ToRepresentableFloat(m11 / determinant),
            ToRepresentableFloat(((m21 * m32) - (m31 * m22)) / determinant),
            ToRepresentableFloat(((m31 * m12) - (m11 * m32)) / determinant));
        EnsureFiniteMatrix(
            inverse,
            "The inverse world transform cannot be represented by a finite Matrix3x2.");
        return inverse;
    }

    private static float ToRepresentableFloat(double value)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
        {
            throw new InvalidOperationException(
                "The inverse world transform cannot be represented by a finite Matrix3x2.");
        }

        var converted = (float)value;
        if (value != 0d && converted == 0f)
        {
            throw new InvalidOperationException(
                "The inverse world transform is smaller than Matrix3x2 can represent.");
        }

        return converted;
    }

    private static void EnsureFiniteMatrix(Matrix3x2 matrix, string message)
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
