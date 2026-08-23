using Najm.Core;
using SkiaSharp;
using CorePixelFormat = Najm.Core.PixelFormat;

namespace Najm.Skia;

/// <summary>
/// Presents an externally owned GL texture as an <see cref="IImage"/>, so a custom GL pipeline is an
/// ordinary drawable that owns its render-to-texture privately.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the second kind of <see cref="IImage"/>.</strong> DEVIATIONS entry 9: an engine
/// snapshot is immutable forever, but an author re-rendering into their own texture every frame
/// cannot promise that and does not need to. What this kind promises is <em>draw stability</em> —
/// the texture's contents do not change for the duration of a draw. Every other lifecycle rule of
/// §5.3 still holds: the image is borrowed, valid only inside the render call that draws it, and
/// never stashed.
/// </para>
/// <para>
/// <strong>Ownership.</strong> <c>SKImage.FromTexture</c> <em>borrows</em>: nothing here creates,
/// reallocates, or deletes the GL texture, and disposing this image leaves the texture intact and
/// re-wrappable. The author owns it for its whole life. The one thing this type does supply is the
/// handshake for deleting it safely: <see cref="TextureReleased"/> fires when Skia has finished with
/// the texture — after this image is disposed <em>and</em> the GPU work referencing it has been
/// flushed and submitted — and deleting a texture Skia still holds is undefined behaviour that a
/// software rasterizer will merely draw black for and a real driver will fault on.
/// </para>
/// <para>
/// <strong>Caching.</strong> The native wrap is built once and reused. It is rebuilt only when the
/// texture is reallocated — a new size or a new interpretation for the same texture id — because a
/// stable-size texture keeps its id and rebuilding per frame would allocate per frame for nothing.
/// The provider hands the same instance back for the same texture id, so an author may call
/// <see cref="GpuSkiaSurfaceProvider.WrapGlTexture(uint, PixelSize)"/> inside their render method
/// without allocating.
/// </para>
/// <para>
/// <strong>Thread affinity.</strong> Bound to the thread holding the provider's GL context current,
/// like everything else on the GPU path.
/// </para>
/// </remarks>
public sealed class GlTextureImage : IImage, ISkiaNativeImage
{
    /// <summary>
    /// Registered once per wrap, static so building a wrap costs no delegate allocation. Skia hands
    /// back the release context, which is the owning image.
    /// </summary>
    private static readonly SKImageTextureReleaseDelegate ReleaseCallback =
        static context => ((GlTextureImage)context).OnTextureReleased();

    private readonly GpuSkiaSurfaceProvider provider;
    private SKImage? wrap;
    private bool disposed;

    internal GlTextureImage(
        GpuSkiaSurfaceProvider provider,
        uint textureId,
        PixelSize size,
        in GlTextureOptions options)
    {
        this.provider = provider;
        TextureId = textureId;
        Rebuild(size, options);
    }

    /// <summary>Gets the GL name of the wrapped texture. It never changes for one wrap.</summary>
    /// <remarks>
    /// A texture reallocated at the same id keeps this wrap; a texture allocated under a
    /// <em>new</em> id is a different wrap, and the old one should be released through
    /// <see cref="GpuSkiaSurfaceProvider.ReleaseGlTexture"/> or <see cref="Dispose"/> before the old
    /// texture is deleted.
    /// </remarks>
    public uint TextureId { get; }

    /// <inheritdoc />
    public PixelSize Size { get; private set; }

    /// <summary>Gets how the texture's storage and contents are currently being interpreted.</summary>
    public GlTextureOptions Options { get; private set; }

    /// <summary>
    /// Gets or sets the callback invoked when Skia has released the texture and deleting it is safe.
    /// </summary>
    /// <remarks>
    /// It is handed <see cref="TextureId"/>. It fires when this image is disposed <em>and</em> the
    /// GPU work that referenced it has been flushed and submitted — not at disposal alone — and it
    /// deletes nothing itself. It also fires when a reallocation replaces the cached wrap, since the
    /// replaced wrap is disposed. A caller that keeps its textures for the environment's lifetime
    /// does not need it at all.
    /// </remarks>
    public Action<uint>? TextureReleased { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// A synchronous GPU readback, and therefore an explicit slow path: it forces the pipeline to
    /// finish before it can answer. It reads the texture's contents at the moment it is called, so
    /// unlike a snapshot it is not a stable record of anything.
    /// </remarks>
    public unsafe void CopyPixels(Span<byte> destination, CorePixelFormat format)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The requested pixel format is not defined.", nameof(format));
        }

        var rowBytes = checked(Size.Width * 4);
        var requiredBytes = checked(rowBytes * Size.Height);
        if (destination.Length < requiredBytes)
        {
            throw new ArgumentException(
                $"Pixel destination requires at least {requiredBytes} bytes but contains {destination.Length}.",
                nameof(destination));
        }

        var (colorType, alphaType) = format switch
        {
            CorePixelFormat.Rgba8888 => (SKColorType.Rgba8888, SKAlphaType.Unpremul),
            CorePixelFormat.Rgba8888Premul => (SKColorType.Rgba8888, SKAlphaType.Premul),
            CorePixelFormat.Bgra8888Premul => (SKColorType.Bgra8888, SKAlphaType.Premul),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        using var outputColorSpace = SKColorSpace.CreateSrgb();
        var outputInfo = new SKImageInfo(Size.Width, Size.Height, colorType, alphaType, outputColorSpace);
        fixed (byte* destinationPointer = destination)
        {
            if (!wrap!.ReadPixels(outputInfo, (IntPtr)destinationPointer, rowBytes, 0, 0))
            {
                throw new InvalidOperationException(
                    "Skia failed to read back the wrapped GL texture in the requested format.");
            }
        }
    }

    /// <summary>
    /// Releases the native wrap and drops it from the provider's cache, leaving the GL texture
    /// itself untouched.
    /// </summary>
    /// <remarks>
    /// Idempotent. The texture is the author's; after this returns, the same id can be wrapped again
    /// and samples correctly. <see cref="TextureReleased"/> fires only once the provider has flushed
    /// and submitted, which is what makes deleting the texture safe.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        provider.ForgetWrap(this);
        wrap?.Dispose();
        wrap = null;
    }

    /// <inheritdoc />
    SKImage ISkiaNativeImage.GetNativeImage()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return wrap!;
    }

    /// <summary>
    /// Returns whether this wrap already describes a texture of this size under this interpretation.
    /// </summary>
    /// <remarks>
    /// Component-wise rather than through a constructed value, and it validates nothing: this is the
    /// per-frame question a warm loop asks, and it must neither allocate nor re-materialize the enum
    /// metadata that <see cref="Enum.IsDefined{TEnum}(TEnum)"/> touches.
    /// </remarks>
    internal bool Describes(PixelSize size, in GlTextureOptions options) =>
        !disposed &&
        Size.Width == size.Width &&
        Size.Height == size.Height &&
        Options.Origin == options.Origin &&
        Options.ColorSpace == options.ColorSpace &&
        Options.IsStraightAlpha == options.IsStraightAlpha &&
        Options.ResolvedTextureTarget == options.ResolvedTextureTarget &&
        Options.ResolvedSizedFormat == options.ResolvedSizedFormat;

    /// <summary>Rebuilds the native wrap after the author reallocated the texture at the same id.</summary>
    internal void Rebuild(PixelSize size, in GlTextureOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var replaced = wrap;
        wrap = provider.CreateNativeWrap(this, TextureId, size, options);
        Size = size;
        Options = options;

        // After the replacement, so a failure to build the new wrap leaves the old one usable.
        replaced?.Dispose();
    }

    private void OnTextureReleased() => TextureReleased?.Invoke(TextureId);

    /// <summary>The release delegate Skia is handed, exposed so the provider registers exactly one.</summary>
    internal static SKImageTextureReleaseDelegate ReleaseDelegate => ReleaseCallback;
}
