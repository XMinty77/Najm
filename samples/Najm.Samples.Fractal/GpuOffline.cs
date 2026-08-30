using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Fractal;

/// <summary>The sample's GL bring-up, and the two runs it drives over the context it keeps.</summary>
/// <remarks>
/// <para>
/// <strong>Most of this file was finding F-1, and F-1 is closed.</strong>
/// <see cref="SkiaOffline.Render"/> and <see cref="SkiaExport.Png"/> now take an
/// <see cref="OfflineBackend"/>, so a scene needing <see cref="RenderCaps.GpuBacked"/> renders
/// through the ordinary entry points and nobody has to assemble a GPU provider to get one.
/// </para>
/// <para>
/// <strong>What is left is what this sample specifically wants.</strong> It holds the context
/// across many renders rather than bringing one up per still — the probe loop renders dozens — and
/// it prints the GL banner, which needs the <see cref="HeadlessGlContext"/> itself and not just the
/// provider over it. Neither is friction the engine should absorb; a sample that reports which
/// rasterizer produced its pixels is a sample doing its job.
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
    /// This used to end in a scratch directory, a <c>File.Move</c> of <c>still_00000.png</c>, and a
    /// <c>try/finally</c> that deleted the directory, because the only public sink that could write a
    /// PNG was the numbered <em>sequence</em> sink — finding F-2. <see cref="FrameSink.PngFile"/>
    /// closed it, and those eleven lines are now one.
    /// </remarks>
    internal long RenderStill(Func<Scene> make, string path, double at, double fps, int sampleCount)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var scene = make() ?? throw new InvalidOperationException("The scene factory returned null.");

        return OfflineRenderer.RenderStill(
            scene,
            provider,
            FrameSink.PngFile(path),
            at,
            fps,
            scale: 1f,
            format: PixelFormat.Rgba8888,
            sampleCount: sampleCount);
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
