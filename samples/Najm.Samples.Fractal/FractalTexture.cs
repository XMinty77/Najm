using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Fractal;

/// <summary>
/// The seam itself: the author's GL pipeline on one side, a Najm <see cref="IImage"/> on the other.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Who does what.</strong> This project creates the GL texture, renders into it, and deletes
/// it. Najm borrows it: <c>WrapGlTexture</c> builds an <c>SKImage.FromTexture</c> that reads the
/// same storage and owns none of it. The wrap is cached per texture id, so
/// <see cref="Acquire"/> from inside a node's <c>Render</c> is a dictionary hit — which is what makes
/// it correct to ask for the image where it is drawn rather than stashing one on the node.
/// </para>
/// <para>
/// <strong>Origin.</strong> <see cref="GlTextureOrigin.BottomLeft"/>, stated rather than defaulted.
/// Row zero of a texture rendered through a framebuffer object is the bottom row of what the shader
/// drew; Skia reads top-left first. The wrap cannot detect this and does not try.
/// </para>
/// <para>
/// <strong>Ordering.</strong> <see cref="Advance"/> submits Skia's outstanding work <em>before</em>
/// overwriting the texture, renders, and then tells Skia its cached GL state is stale. The middle
/// step is the author's obligation; the other two are the ones that are easy to leave out and
/// impossible to notice missing. See NOTES.md F-6 and F-7.
/// </para>
/// </remarks>
internal sealed class FractalTexture(GpuSkiaSurfaceProvider provider, FractalGpu pipeline) : IDisposable
{
    private static readonly GlTextureOptions Options = new()
    {
        // GL renders bottom-up. Anything drawn through a framebuffer object is bottom-left.
        Origin = GlTextureOrigin.BottomLeft,
    };

    private bool disposed;

    /// <summary>Renders the next frame into the texture and leaves Skia's GL state honest.</summary>
    internal void Advance(in FractalUniforms uniforms)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        // Nothing in the engine promises that work recorded against this texture last frame has
        // been submitted, so submit it before the texture stops being what that work described.
        provider.Flush(submit: true);

        pipeline.Render(uniforms);

        // Mandatory. Skia caches what it believes the GL state machine holds, and the pipeline above
        // has just changed every part of it Skia cares about.
        provider.ResetGlState();
    }

    /// <summary>Gets the wrap for the texture as it currently stands.</summary>
    internal IImage Acquire()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return provider.WrapGlTexture(pipeline.TextureId, pipeline.Size, Options);
    }

    /// <summary>
    /// Releases Najm's wrap, flushes so the release handshake fires, and only then deletes the
    /// texture.
    /// </summary>
    /// <remarks>
    /// The order is the whole point. Deleting a texture Skia still references is undefined
    /// behaviour; llvmpipe would draw black for it and a real driver would fault. The engine's
    /// handshake — <c>GlTextureImage.TextureReleased</c> — exists to say when the deletion is safe,
    /// and a caller whose textures live as long as the environment, like this one, gets the same
    /// guarantee from release-then-flush-then-delete.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        provider.ReleaseGlTexture(pipeline.TextureId);
        provider.Flush(submit: true);
        pipeline.Dispose();
    }
}
