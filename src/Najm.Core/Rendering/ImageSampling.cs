namespace Najm.Core;

/// <summary>Specifies portable sampling for an affine image draw.</summary>
public enum ImageSampling
{
    /// <summary>Linearly interpolates neighboring source pixels without mipmaps.</summary>
    Linear,

    /// <summary>Selects the nearest source pixel without interpolation.</summary>
    Nearest,
}
