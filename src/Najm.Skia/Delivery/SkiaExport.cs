using Najm.Core;
using Najm.Core.Text;

namespace Najm.Skia;

/// <summary>Exports a single frame of a scene at a chosen time.</summary>
/// <remarks>
/// <para>
/// With no live preview yet, a rendered PNG is how a change is seen at all, so this is part of the
/// working loop rather than a delivery convenience. Vector exports join it here when the writers
/// land.
/// </para>
/// <para>
/// It renders through whichever <see cref="OfflineBackend"/> is asked for, so the inspection loop is
/// available to a GPU scene on the same terms as a raster one. That was not true before: the export
/// route built a raster provider in its own body, which made the one route an author is told to use
/// the one route a GL-interop scene could not use.
/// </para>
/// </remarks>
public static class SkiaExport
{
    /// <summary>Renders a fresh scene instance at <paramref name="at"/> seconds and writes a PNG.</summary>
    /// <param name="make">
    /// The scene factory. A factory rather than an instance because the export evaluates a fresh
    /// instance: seeking is running the scene forward from load, so a reused instance would already
    /// be past the requested time.
    /// </param>
    /// <param name="path">The PNG file to write. Its directory is created if absent.</param>
    /// <param name="at">The finite, non-negative simulated time to render, in seconds.</param>
    /// <param name="framesPerSecond">The fixed rate the seek is quantized against. The default is 60.</param>
    /// <param name="scale">
    /// The virtual-to-output pixel scale. The default is one, so a 1920×1080 scene writes a
    /// 1920×1080 file.
    /// </param>
    /// <param name="typesetter">
    /// The typesetter the scene measures and draws text through, or null for the fail-loud
    /// <see cref="NullTypesetter"/>. Pass <c>new Najm.Text.Typesetter()</c> to export a scene with
    /// any text in it; without one, the first text node fails at attach and says which option to set.
    /// </param>
    /// <param name="backend">
    /// Which Skia backend to render through. The default is CPU raster; pass
    /// <see cref="OfflineBackend.Gpu"/> for a scene that needs a GPU-backed target. Read that
    /// member's remarks before doing so — the GPU path brings up a GL context on the calling thread,
    /// and it is not the configuration goldens are taken on.
    /// </param>
    /// <param name="sampleCount">
    /// The requested surface sample count. The default is one, which is the only value CPU raster
    /// has: raster Skia is analytically antialiased and normalizes every count to one. It becomes
    /// real on <see cref="OfflineBackend.Gpu"/>, where one sample means no antialiasing at all and
    /// four is the usual answer for a figure with geometry in it.
    /// </param>
    /// <returns>The number of ticks run before the frame was rendered, which is <c>ceil(at × fps)</c>.</returns>
    /// <remarks>
    /// <para>
    /// The seek is <c>ceil(at × fps)</c> ticks followed by one render — the same frame the offline
    /// loop would emit at that time, not an interpolation.
    /// </para>
    /// <para>
    /// <strong>At <c>at: 0</c> the tick count is zero.</strong> The exported frame is the loaded
    /// state, and <c>OnStart</c> does not run at all, because it runs inside the first tick. A scene
    /// that builds its content in <c>OnStart</c> exports empty at zero and populated at any positive
    /// time; that is the contract, not a bug.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is whitespace, or <paramref name="backend"/> is not a defined backend.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="at"/>, <paramref name="framesPerSecond"/>, <paramref name="scale"/>, or
    /// <paramref name="sampleCount"/> is out of range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The factory returned null, or the GPU backend was asked for and no GL context could be
    /// brought up.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// <see cref="OfflineBackend.Gpu"/> was asked for on a platform with no headless GL context.
    /// </exception>
    public static long Png(
        Func<Scene> make,
        string path,
        double at,
        double framesPerSecond = 60d,
        float scale = 1f,
        ITypesetter? typesetter = null,
        OfflineBackend backend = OfflineBackend.Raster,
        int sampleCount = 1)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var scene = make()
            ?? throw new InvalidOperationException("The scene factory returned null.");

        using var surfaces = OfflineSurfaces.Create(backend);
        var sink = new PngFileFrameSink(path);
        return OfflineRenderer.RenderStill(
            scene,
            surfaces,
            sink,
            at,
            framesPerSecond,
            scale,
            sampleCount: sampleCount,
            typesetter: typesetter);
    }
}
