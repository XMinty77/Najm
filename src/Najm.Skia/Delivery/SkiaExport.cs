using Najm.Core;
using Najm.Core.Text;

namespace Najm.Skia;

/// <summary>Exports a single frame of a scene at a chosen time.</summary>
/// <remarks>
/// With no live preview yet, a rendered PNG is how a change is seen at all, so this is part of the
/// working loop rather than a delivery convenience. Vector exports join it here when the writers
/// land.
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
    /// <exception cref="ArgumentException"><paramref name="path"/> is whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="at"/>, <paramref name="framesPerSecond"/>, or <paramref name="scale"/> is out
    /// of range.
    /// </exception>
    /// <exception cref="InvalidOperationException">The factory returned null.</exception>
    public static long Png(
        Func<Scene> make,
        string path,
        double at,
        double framesPerSecond = 60d,
        float scale = 1f,
        ITypesetter? typesetter = null)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var scene = make()
            ?? throw new InvalidOperationException("The scene factory returned null.");

        using var surfaces = new RasterSkiaSurfaceProvider();
        var sink = new PngFileFrameSink(path);
        return OfflineRenderer.RenderStill(
            scene,
            surfaces,
            sink,
            at,
            framesPerSecond,
            scale,
            typesetter: typesetter);
    }
}
