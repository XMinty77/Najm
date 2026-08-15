namespace Najm.Core;

/// <summary>Identifies a tightly packed 8-bit pixel layout used for explicit readback.</summary>
public enum PixelFormat
{
    /// <summary>Red, green, blue, alpha bytes with unpremultiplied color channels.</summary>
    Rgba8888,

    /// <summary>Red, green, blue, alpha bytes with premultiplied color channels.</summary>
    Rgba8888Premul,

    /// <summary>Blue, green, red, alpha bytes with premultiplied color channels.</summary>
    Bgra8888Premul,
}
