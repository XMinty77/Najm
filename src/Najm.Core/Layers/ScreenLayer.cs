namespace Najm.Core;

/// <summary>Provides a Y-down virtual-coordinate layer with a permanent two-dimensional root.</summary>
/// <remarks>
/// <para>
/// This is the right default. Its coordinates already are virtual coordinates, so a node placed at
/// <c>(960, 540)</c> paints at the middle of a 1920×1080 scene and stays there — no camera, no Y
/// flip, nothing between what an author writes and where it lands. Panels, plots, diagrams, HUDs,
/// captions, and titles all belong here, and so does a whole scene that never scrolls, however
/// elaborate or however animated its contents are.
/// </para>
/// <para>
/// Choose <see cref="WorldLayer2D"/> instead when the scene behaves like a world — content persisting
/// off-screen in a space larger than the frame, with a camera moving through it — and see that type
/// for the full test. A scene often wants both: the world below, and a screen layer above it for the
/// overlay that must not travel with the camera.
/// </para>
/// </remarks>
public class ScreenLayer : Layer
{
    /// <summary>Creates an empty screen-space layer.</summary>
    public ScreenLayer()
    {
        Root = new Node2D();
        Root.AssignLayerRoot(this);
    }

    /// <summary>Gets the permanent root of this layer's node subtree.</summary>
    public Node2D Root { get; }

    /// <inheritdoc />
    public sealed override bool YAxisPointsUp => false;

    /// <inheritdoc />
    protected sealed override Node RootNode => Root;
}
