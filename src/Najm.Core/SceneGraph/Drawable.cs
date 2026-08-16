namespace Najm.Core;

/// <summary>A two-dimensional node that paints portable Tier-1 geometry on any backend.</summary>
/// <remarks>
/// Subclassing this type and implementing <see cref="Render"/> is the first-class way to author
/// something that draws. A drawable states its local extent through
/// <see cref="Node2D.GeometryBounds"/> and its companions so the render traverser can cull, gate
/// input, and size isolation without knowing what the node paints.
/// </remarks>
public abstract class Drawable : Node2D
{
    /// <inheritdoc />
    public abstract override void Render(IDrawContext2D context);
}
