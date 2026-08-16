namespace Najm.Core;

/// <summary>Provides a Y-up world-coordinate layer framed by a two-dimensional camera.</summary>
/// <remarks>
/// The layer is usable the moment it is constructed: it owns a permanent root and a default
/// <see cref="Camera2D"/> already attached to that root.
/// </remarks>
public class WorldLayer2D : Layer
{
    private Camera2D camera;

    /// <summary>Creates an empty world-space layer with a default camera at the world origin.</summary>
    public WorldLayer2D()
    {
        Root = new Node2D();
        Root.AssignLayerRoot(this);
        camera = new Camera2D();
        Root.Add(camera);
    }

    /// <summary>Gets the permanent root of this layer's node subtree.</summary>
    public Node2D Root { get; }

    /// <summary>
    /// Gets or sets the camera framing this layer. It is never null, and assigning a parentless
    /// camera attaches it to <see cref="Root"/> as a convenience.
    /// </summary>
    /// <remarks>
    /// A camera that already has a parent is used exactly where it sits, so a camera can ride any
    /// node in this layer's subtree.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public Camera2D Camera
    {
        get => camera;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(camera, value))
            {
                return;
            }

            if (value.Parent is null)
            {
                Root.Add(value);
            }

            camera = value;
        }
    }

    /// <inheritdoc />
    public sealed override bool YAxisPointsUp => true;

    /// <inheritdoc />
    protected sealed override Node RootNode => Root;
}
