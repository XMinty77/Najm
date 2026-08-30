using Najm.Core;

namespace Najm.Skia;

/// <summary>Selects which Skia backend an offline entry point assembles behind the Core loop.</summary>
/// <remarks>
/// <para>
/// The offline drivers used to construct <see cref="RasterSkiaSurfaceProvider"/> in their own
/// bodies, which made the documented export route unable to render the one kind of scene the GPU
/// seam exists for. This enum is the seam that was missing: it names a provider, the entry point
/// builds it, and the loop underneath is the same deterministic loop either way — Core's
/// <see cref="OfflineRenderer"/> has always been happy with any <see cref="ISurfaceProvider"/>.
/// </para>
/// <para>
/// <strong>The two are not interchangeable, and the difference is not only speed.</strong> Raster
/// Skia is analytically antialiased, so <c>SampleCount</c> is normalized to one and geometry is
/// smooth at any count. A GPU surface antialiases by multisampling, so the same scene rendered at
/// one sample has visibly harder edges than the raster render of it; ask for four. Content is not
/// interchangeable either: a drawable holding a wrapped GL texture is legal on
/// <see cref="Gpu"/> and refused everywhere else, and a scene built out of ordinary paths and text
/// has no reason to leave <see cref="Raster"/>.
/// </para>
/// </remarks>
public enum OfflineBackend
{
    /// <summary>CPU-raster Skia. The default, and the configuration determinism is pinned on.</summary>
    /// <remarks>
    /// Needs no GL stack, no driver, and no display, so it runs anywhere the process runs, and two
    /// runs of one scene are byte-identical on any machine — which is what makes it the backend the
    /// golden images and the determinism hashes are taken on.
    /// </remarks>
    Raster = 0,

    /// <summary>
    /// GPU Skia over a headless OpenGL ES context the entry point creates, uses, and disposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the configuration that advertises <see cref="RenderCaps.GpuBacked"/>, and therefore
    /// the only one on which a scene sampling an author-owned GL texture can render at all.
    /// </para>
    /// <para>
    /// <strong>Linux only, and it fails loudly elsewhere.</strong> The context comes from
    /// <see cref="HeadlessGlContext"/>, which binds EGL's surfaceless platform directly; every other
    /// platform throws <see cref="PlatformNotSupportedException"/> from the entry point rather than
    /// at some later loader boundary. A software rasterizer such as llvmpipe satisfies it fine — the
    /// requirement is a GL ES 3.0 driver, not a GPU.
    /// </para>
    /// <para>
    /// <strong>Everything happens on the calling thread.</strong> The GL context is made current
    /// there and the provider binds to it, so the scene's own GL work — which belongs in its tick,
    /// not in a <c>Render</c> that Skia may replay later — happens on that thread too. Do not hand
    /// the scene factory something that renders on a thread pool.
    /// </para>
    /// <para>
    /// <strong>Determinism is narrower here.</strong> Two runs on one machine agree; two machines
    /// with different drivers need not, because rasterization rules, multisample resolve, and
    /// floating-point contraction are the driver's business. Frames from this backend are worth
    /// comparing against each other and are not worth checking into a golden set.
    /// </para>
    /// </remarks>
    Gpu = 1,
}
