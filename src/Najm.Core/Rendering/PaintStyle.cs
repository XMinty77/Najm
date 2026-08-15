namespace Najm.Core;

/// <summary>Specifies whether path geometry is filled or stroked.</summary>
public enum PaintStyle
{
    /// <summary>Fill the path interior using its <see cref="FillRule"/>.</summary>
    Fill,

    /// <summary>Stroke the path centerline.</summary>
    Stroke,
}
