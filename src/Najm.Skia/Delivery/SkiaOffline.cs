using Najm.Core;

namespace Najm.Skia;

/// <summary>Renders a scene offline through CPU-raster Skia.</summary>
/// <remarks>
/// This is the convenience over <see cref="OfflineRenderer"/>: it assembles the raster backend —
/// <see cref="RasterSkiaSurfaceProvider"/>, and therefore <see cref="SkiaCompositor"/> — and hands
/// the loop to Core. Determinism hashes are taken on exactly this configuration.
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
    /// <returns>The number of frames submitted.</returns>
    /// <remarks>
    /// Every tick receives the canonical empty input block, so the run is deterministic: two calls
    /// with the same factory and options produce byte-identical frames.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The factory returned null, or the options specify no length.
    /// </exception>
    public static long Render(Func<Scene> make, OfflineOptions options)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentNullException.ThrowIfNull(options);

        var scene = make()
            ?? throw new InvalidOperationException("The scene factory returned null.");

        // Disposed after the render, which unloads the scene and with it the compositor the
        // provider created: targets never outlive their provider.
        using var surfaces = new RasterSkiaSurfaceProvider();
        return OfflineRenderer.Render(scene, surfaces, options);
    }
}
