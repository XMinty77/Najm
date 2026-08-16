namespace Najm.Core;

/// <summary>Specifies the portable brush subset shared by every rendering backend.</summary>
public enum BrushKind
{
    /// <summary>Paints one flat color.</summary>
    Solid,

    /// <summary>Interpolates gradient stops along the segment between two local-unit points.</summary>
    LinearGradient,

    /// <summary>Interpolates gradient stops outward from a local-unit center to a local-unit radius.</summary>
    RadialGradient,

    /// <summary>Tiles an image handle across the painted geometry.</summary>
    ImagePattern,
}
