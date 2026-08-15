namespace Najm.Core;

/// <summary>Describes target capabilities that backend-specific content may require.</summary>
[Flags]
public enum RenderCaps
{
    /// <summary>No backend-specific capability is promised.</summary>
    None = 0,

    /// <summary>The target is implemented by Skia and accepts Skia-specific drawables.</summary>
    SkiaSurface = 1 << 0,

    /// <summary>The target emits vector content rather than ordinary raster pixels.</summary>
    VectorTarget = 1 << 1,

    /// <summary>The target is backed by a GPU resource.</summary>
    GpuBacked = 1 << 2,
}
