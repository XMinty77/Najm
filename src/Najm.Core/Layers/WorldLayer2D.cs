using System.Numerics;

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

    /// <summary>
    /// Centers this layer's <see cref="Camera"/> on a world rectangle and zooms so the whole
    /// rectangle fits the extent this layer frames.
    /// </summary>
    /// <param name="worldRect">The finite world rectangle to frame, with positive width and height.</param>
    /// <remarks>
    /// <para>
    /// This is ARCHITECTURE Appendix B.2's one-argument <c>layer.Camera.FitRect(rect)</c>, moved to
    /// the layer because the layer is what knows the extent. It forwards to
    /// <see cref="Camera2D.FitRect(in Rect, in System.Numerics.Vector2)"/> and adds nothing else:
    /// the fit rule, the rotation handling, and the centering are all that method's.
    /// </para>
    /// <para>
    /// <strong>The extent is this layer's, not the scene's.</strong> A layer with a
    /// <see cref="Layer.Viewport"/> frames that viewport's size; a full-frame layer frames
    /// <see cref="Scene.VirtualResolution"/>. That is exactly the distinction
    /// <see cref="RenderTraverser.ComputeLayerBase(Layer, in System.Numerics.Vector2, float)"/>
    /// makes when it builds the camera's mapping, and fitting against anything else would frame a
    /// rectangle the render then crops: a half-width viewport shown a full-frame fit sees the right
    /// half of the rectangle disappear.
    /// </para>
    /// <para>
    /// A full-frame layer therefore needs a scene to answer at all, and one that has none says so
    /// rather than inventing a resolution. A viewport'd layer owns its extent outright and needs no
    /// scene.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This layer frames the whole frame and belongs to no scene, so no virtual resolution exists to
    /// fit against.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The rectangle is degenerate or not finite, or the resulting zoom is not finite and greater
    /// than zero.
    /// </exception>
    public void FitRect(in Rect worldRect) => camera.FitRect(worldRect, FramedExtent);

    /// <inheritdoc />
    public sealed override bool YAxisPointsUp => true;

    /// <inheritdoc />
    protected sealed override Node RootNode => Root;

    /// <summary>
    /// Gets the extent this layer's camera frames: its viewport's size when it has one, and its
    /// scene's virtual resolution otherwise.
    /// </summary>
    /// <remarks>
    /// The scene is resolved from the stack that owns this layer rather than from attachment, so
    /// the answer is the same before load as after it — an author who builds a scene's layers in a
    /// constructor and frames them there gets the same extent the render will use.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The extent would have to come from a scene and this layer has none.
    /// </exception>
    private Vector2 FramedExtent
    {
        get
        {
            if (Viewport is { } viewport)
            {
                return new Vector2(viewport.Width, viewport.Height);
            }

            var scene = AttachedScene ?? OwnerStack?.Owner ?? ReservationStack?.Owner;
            return scene is not null
                ? scene.VirtualResolution
                : throw new InvalidOperationException(
                    "WorldLayer2D.FitRect frames this layer's extent, and a full-frame layer's " +
                    "extent is its scene's VirtualResolution. This layer belongs to no scene, so " +
                    "there is no resolution to fit against: add the layer to a scene first, give " +
                    "it a Viewport, or call Camera.FitRect(rect, virtualResolution) with the " +
                    "extent you mean.");
        }
    }
}
