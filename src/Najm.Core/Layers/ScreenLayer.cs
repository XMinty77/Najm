namespace Najm.Core;

/// <summary>Provides a Y-down virtual-coordinate layer with a permanent two-dimensional root.</summary>
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
