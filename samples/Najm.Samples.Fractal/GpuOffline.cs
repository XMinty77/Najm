using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Fractal;

/// <summary>
/// The GPU half of <c>SkiaOffline</c> / <c>SkiaExport</c>, which the engine does not ship.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This file should not exist.</strong> <see cref="SkiaOffline.Render"/> and
/// <see cref="SkiaExport.Png"/> construct <c>RasterSkiaSurfaceProvider</c> inside their own bodies,
/// so neither can render a scene that needs <see cref="RenderCaps.GpuBacked"/> — which is every
/// scene the external-texture seam exists for. <see cref="OfflineRenderer"/> in Core takes an
/// <see cref="ISurfaceProvider"/> and is entirely happy with a GPU one; only the assembly is
/// missing. See NOTES.md finding F-1.
/// </para>
/// <para>
/// <strong>Dispose order is the reason this is a class and not two loose methods.</strong> The
/// <c>GRContext</c> must be released while its GL context is still alive. Passing
/// <c>ownsGlContext: true</c> to <see cref="GpuSkiaSurfaceProvider.CreateOver"/> hands that ordering
/// to the provider, which is the only way to get it right without thinking about it every time.
/// </para>
/// </remarks>
internal sealed class GpuOffline : IDisposable
{
    private readonly GpuSkiaSurfaceProvider provider;
    private bool disposed;

    private GpuOffline(GpuSkiaSurfaceProvider provider, string description)
    {
        this.provider = provider;
        Description = description;
    }

    /// <summary>Gets a one-line description of the GL stack that was brought up.</summary>
    internal string Description { get; }

    /// <summary>Gets the provider, for a caller that needs its GPU limits before rendering.</summary>
    internal GpuSkiaSurfaceProvider Provider => provider;

    /// <summary>
    /// Brings up a headless GL context on the calling thread and a Skia GPU provider over it.
    /// </summary>
    /// <remarks>
    /// The context is made current by <see cref="HeadlessGlContext.Create"/> on the thread that
    /// calls it, and <see cref="GpuSkiaSurfaceProvider"/> records that thread. Everything after this
    /// — the author's GL, the wrap, every engine draw — must stay on it.
    /// </remarks>
    internal static GpuOffline Create()
    {
        var glContext = HeadlessGlContext.Create();
        var provider = GpuSkiaSurfaceProvider.CreateOver(glContext, ownsGlContext: true);
        var description =
            $"{glContext.Renderer.Trim()} | {glContext.Version.Trim()} | "
            + $"GLSL {glContext.ShadingLanguageVersion.Trim()} | max texture {provider.MaxTextureSize}";
        return new GpuOffline(provider, description);
    }

    /// <summary>Runs a fresh scene instance through the deterministic offline loop on the GPU.</summary>
    internal long Render(Func<Scene> make, OfflineOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scene = make() ?? throw new InvalidOperationException("The scene factory returned null.");
        return OfflineRenderer.Render(scene, provider, options);
    }

    /// <summary>Renders one frame at a chosen time and writes it to a named PNG.</summary>
    /// <remarks>
    /// The file-system shuffle at the end is finding F-2: <c>PngFileFrameSink</c> is internal and
    /// <c>SkiaExport.Png</c> is raster-only, so the only public route to a PNG is the
    /// <em>sequence</em> sink, which names its one frame <c>still_00000.png</c>.
    /// </remarks>
    internal long RenderStill(Func<Scene> make, string path, double at, double fps, int sampleCount)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scene = make() ?? throw new InvalidOperationException("The scene factory returned null.");

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var scratch = Path.Combine(Path.GetTempPath(), $"najm-still-{Guid.NewGuid():N}");
        try
        {
            var sink = FrameSink.PngSequence(scratch, "still");
            var ticks = OfflineRenderer.RenderStill(
                scene,
                provider,
                sink,
                at,
                fps,
                scale: 1f,
                format: PixelFormat.Rgba8888,
                sampleCount: sampleCount);

            var written = Path.Combine(scratch, "still_00000.png");
            File.Move(written, full, overwrite: true);
            return ticks;
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
    }

    /// <summary>Disposes the provider, which disposes the GPU context and then the GL context.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        provider.Dispose();
    }
}
