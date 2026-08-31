using Najm.Core;

namespace Najm.Skia;

/// <summary>Renders a scene offline through Skia, on the CPU or on the GPU.</summary>
/// <remarks>
/// <para>
/// This is the convenience over <see cref="OfflineRenderer"/>: it assembles a backend — a provider,
/// and therefore a <see cref="SkiaCompositor"/> — and hands the loop to Core. The default backend is
/// <see cref="OfflineBackend.Raster"/>, and determinism hashes are taken on exactly that
/// configuration.
/// </para>
/// <para>
/// <strong>The GPU backend is a parameter, not a second method</strong>, because nothing else about
/// the run changes: same loop, same options, same sink, same frame indices. What changes is which
/// scenes are renderable at all — <see cref="OfflineBackend.Gpu"/> is the only offline
/// configuration whose environment reports <see cref="RenderCaps.GpuBacked"/>, so a scene sampling
/// an author-owned GL texture renders there and is refused at attach anywhere else.
/// </para>
/// <para>
/// It deliberately takes no typesetter of its own: <see cref="OfflineOptions.Typesetter"/> already
/// carries one, and a second way to supply the same capability is a second thing to keep in step. A
/// scene with text in it sets that option and this method passes it through untouched.
/// </para>
/// </remarks>
public static class SkiaOffline
{
    /// <summary>Runs a fresh scene instance at a fixed step and delivers every frame to a sink.</summary>
    /// <param name="make">
    /// The scene factory. A factory rather than an instance because a render consumes the scene it
    /// runs: the instance is loaded, ticked to the end, and unloaded, so a second run needs a second
    /// instance. It is also what makes two runs comparable — a fresh instance has no state carried
    /// over from the first.
    /// </param>
    /// <param name="options">The rate, length, output size, and sink for this run.</param>
    /// <param name="backend">
    /// Which Skia backend to assemble. The default is CPU raster; pass
    /// <see cref="OfflineBackend.Gpu"/> for a scene that needs a GPU-backed target, and read that
    /// member's remarks first — it brings up a GL context on this thread and it narrows what
    /// determinism means.
    /// </param>
    /// <returns>The number of frames submitted.</returns>
    /// <remarks>
    /// <para>
    /// Every tick receives the canonical empty input block, so the run is deterministic: two calls
    /// with the same factory and options produce byte-identical frames. On
    /// <see cref="OfflineBackend.Gpu"/> that promise holds between two runs on one machine and stops
    /// there, because the pixels are the driver's.
    /// </para>
    /// <para>
    /// The scene is built before the backend is, so a factory that throws costs nothing and never
    /// leaves a GL context behind.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="backend"/> is not a defined backend.</exception>
    /// <exception cref="InvalidOperationException">
    /// The factory returned null, an open-ended run passed
    /// <see cref="OfflineOptions.MaxFrames"/> with the scene's work unfinished, or the GPU backend
    /// was asked for and no GL context could be brought up.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// <see cref="OfflineBackend.Gpu"/> was asked for on a platform with no headless GL context.
    /// </exception>
    public static long Render(
        Func<Scene> make,
        OfflineOptions options,
        OfflineBackend backend = OfflineBackend.Raster)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentNullException.ThrowIfNull(options);

        var scene = make()
            ?? throw new InvalidOperationException("The scene factory returned null.");

        // Disposed after the render, which unloads the scene and with it the compositor the
        // provider created: targets never outlive their provider. On the GPU backend this one
        // dispose also releases the Skia GPU context and then the GL context, in that order.
        using var surfaces = OfflineSurfaces.Create(backend);
        return OfflineRenderer.Render(scene, surfaces, options);
    }
}
