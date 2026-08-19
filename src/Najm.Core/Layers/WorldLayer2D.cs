namespace Najm.Core;

/// <summary>Provides a Y-up world-coordinate layer framed by a two-dimensional camera.</summary>
/// <remarks>
/// <para>
/// The layer is usable the moment it is constructed: it owns a permanent root and a default
/// <see cref="Camera2D"/> already attached to that root.
/// </para>
/// <para>
/// <strong>When this layer earns its keep.</strong> When the scene behaves <em>like a world</em>:
/// its elements persist at their own positions in a space larger than the frame, and the camera
/// moves through that space. The test is not "is this physical" or "is this animated" — it is
/// whether there is somewhere to go. Plot a harmonic series across the whole frame, put text and
/// annotation around the origin, then fly the camera off toward infinity and bring it back: that is
/// a world, because the content stayed put and the viewpoint did the travelling. An orrery is a
/// world: zoom to one planet and track alongside it while its name sits in a corner and the rest of
/// the system keeps orbiting, consistently, off-screen. Timeline animations that travel along their
/// own axis, and phase-diagram animations that roam a state space, are worlds for the same reason.
/// </para>
/// <para>
/// <strong>When it does not.</strong> A diagram that simply sits there wants a
/// <see cref="ScreenLayer"/>. If nothing ever leaves the frame and no camera ever moves, the camera
/// and the Y flip buy an indirection and no expressive power, and the author ends up mapping data to
/// pixels by hand on both sides of it. Reach for this layer at the moment a camera move or an
/// off-screen extent enters the design — not because the subject sounds like physics.
/// </para>
/// <para>
/// The two mix freely in one scene, and usually should: the world goes in a
/// <see cref="WorldLayer2D"/> and the overlay that must stay put — the corner label naming the
/// planet the camera is tracking — goes in a <see cref="ScreenLayer"/> above it.
/// </para>
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
    /// node in this layer's subtree: framing derives from the camera's world transform, not its
    /// local one, so a camera parented under a moving rig frames from where the rig has carried it.
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
