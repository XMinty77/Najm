namespace Najm.Core;

/// <summary>
/// Identifies the mandatory color-space tag carried by every render surface.
/// </summary>
public enum ColorSpace
{
    /// <summary>IEC 61966-2-1 sRGB with its encoded transfer function.</summary>
    Srgb,

    /// <summary>Linear-light sRGB primaries.</summary>
    LinearSrgb,
}
