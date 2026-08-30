using System.Runtime.CompilerServices;
using Najm.Core;
using SkiaSharp;
using CoreColorSpace = Najm.Core.ColorSpace;

namespace Najm.Skia;

/// <summary>Creates top-left-origin GPU Skia targets over one <see cref="GRContext"/>.</summary>
/// <remarks>
/// <para>
/// <strong>Division of labour.</strong> This provider does not create GL contexts. ARCHITECTURE
/// §4.6 gives that job to the host, which brings GL up its own way and constructs a provider over
/// the context it already owns; offline rendering has no host, so
/// <see cref="HeadlessGlContext"/> supplies one and <see cref="CreateOver"/> bolts the two together.
/// Either way the provider's business starts at the <see cref="GRContext"/>.
/// </para>
/// <para>
/// <strong>How a scene reaches this provider, spelled out because every author on this seam
/// writes it and no document has shown it.</strong> A scene is handed an
/// <see cref="ISurfaceProvider"/>, and the interop operations —
/// <see cref="WrapGlTexture(uint, PixelSize)"/>, <see cref="ResetGlState"/>,
/// <see cref="Flush(bool)"/> — are on this class, not on that interface. Reaching them is a
/// downcast, in <c>OnLoad</c>, and it is two steps rather than one:
/// <code>
/// protected override void OnLoad()
/// {
///     if (!Env.Surfaces.Caps.HasFlag(RenderCaps.GpuBacked))
///     {
///         throw new InvalidOperationException(
///             "This scene renders through a GL texture and needs a GPU-backed target.");
///     }
///
///     var gpu = (GpuSkiaSurfaceProvider)Env.Surfaces;
///     // ... build the author's GL pipeline against gpu, here and nowhere earlier.
/// }
/// </code>
/// The capability check first, and it is not ceremony around the cast: it is the check NAJM-SKIA I.7
/// specifies, it is the one whose failure message is about the <em>target</em> rather than about a
/// type name, and it stays correct when a second GPU backend exists and the cast does not. The cast
/// second, for access only. No engine helper wraps this — a convenience over a language cast is a
/// second name for a cast — but the order matters and this is the order.
/// </para>
/// <para>
/// <strong>Do it in <c>OnLoad</c>, not in <c>Render</c>.</strong> <c>OnLoad</c> is the first moment
/// <see cref="SceneEnvironment"/> exists and the last one before any frame has been paid for. A
/// scene that discovers in <c>Render</c> that its target cannot do GL has already been loaded,
/// ticked, and half drawn, and it will discover it again on every frame after.
/// </para>
/// <para>
/// <strong>Sample counts become real here.</strong> A raster provider normalizes every requested
/// count to one because CPU raster has no multisample render-target axis. A GPU target does, so this
/// provider normalizes through <see cref="SurfaceSpec.NormalizeForGpu"/> against the device maximum
/// for the target's color type instead — see that method for the rule and why it rounds down.
/// </para>
/// <para>
/// <strong>Thread affinity, and the failure mode to know about.</strong> The provider is bound to
/// the thread that holds its GL context current, and it records that thread at construction. Every
/// entry point checks it, because the failure it prevents is invisible: Skia GPU work issued while
/// the context is current on some <em>other</em> thread throws nothing, logs nothing, and produces
/// transparent black. A loud exception naming both threads is worth the integer comparison. Moving
/// the provider deliberately means making the context current on the new thread and calling
/// <see cref="Rebind"/>.
/// </para>
/// <para>
/// <strong>Surfaces are unbudgeted.</strong> Every surface this provider creates passes
/// <c>budgeted: false</c>: the engine accounts its own surfaces and Skia's resource-cache budget is
/// left for Skia-internal allocations — glyph atlases, <c>saveLayer</c> layers, path masks.
/// </para>
/// </remarks>
public sealed class GpuSkiaSurfaceProvider : ISurfaceProvider
{
    private readonly Dictionary<uint, GlTextureImage> wraps = [];
    private readonly bool ownsContext;
    private readonly GRGlInterface? ownedInterface;
    private readonly HeadlessGlContext? ownedGlContext;
    private readonly SKColorSpace srgb = SKColorSpace.CreateSrgb();
    private readonly SKColorSpace linearSrgb = SKColorSpace.CreateSrgbLinear();
    private GRContext? context;
    private int ownerThreadId;

    /// <summary>Creates a provider over a live <see cref="GRContext"/>.</summary>
    /// <param name="context">
    /// The GPU context, whose GL context must be current on the calling thread.
    /// </param>
    /// <param name="ownsContext">
    /// Whether disposing this provider disposes <paramref name="context"/>. A host that created the
    /// context keeps ownership and passes false; <see cref="CreateOver"/> passes true.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="context"/> has been abandoned.</exception>
    public GpuSkiaSurfaceProvider(GRContext context, bool ownsContext)
        : this(context, ownsContext, ownedInterface: null, ownedGlContext: null)
    {
    }

    private GpuSkiaSurfaceProvider(
        GRContext context,
        bool ownsContext,
        GRGlInterface? ownedInterface,
        HeadlessGlContext? ownedGlContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsAbandoned)
        {
            throw new ArgumentException(
                "The GPU context has been abandoned and can no longer create surfaces.",
                nameof(context));
        }

        this.context = context;
        this.ownsContext = ownsContext;
        this.ownedInterface = ownedInterface;
        this.ownedGlContext = ownedGlContext;
        ownerThreadId = Environment.CurrentManagedThreadId;
        MaxTextureSize = context.MaxTextureSize;
    }

    /// <summary>Builds a Skia GPU context over a headless GL context and a provider over that.</summary>
    /// <param name="glContext">The current headless context to build the Skia GPU context on.</param>
    /// <param name="ownsGlContext">
    /// Whether disposing this provider also disposes <paramref name="glContext"/>. The dispose order
    /// matters and this is the safe way to get it: the <see cref="GRContext"/> is released while its
    /// GL context is still alive, never after.
    /// </param>
    /// <returns>A provider owning the Skia GPU context it just created.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glContext"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Skia could not build a GL interface or a GPU context over <paramref name="glContext"/>; the
    /// message names which step failed and what the context reports itself as.
    /// </exception>
    public static GpuSkiaSurfaceProvider CreateOver(HeadlessGlContext glContext, bool ownsGlContext = false)
    {
        ArgumentNullException.ThrowIfNull(glContext);

        var glInterface = GRGlInterface.CreateGles(glContext.GetProcAddress);
        if (glInterface is null || !glInterface.Validate())
        {
            glInterface?.Dispose();
            if (ownsGlContext)
            {
                glContext.Dispose();
            }

            throw new InvalidOperationException(
                "Skia could not build a valid GL ES interface over this context "
                + $"(renderer '{glContext.Renderer}', version '{glContext.Version}'). Najm's GPU "
                + "provider requires OpenGL ES 3.0 or a compatible desktop GL profile.");
        }

        var gpuContext = GRContext.CreateGl(glInterface);
        if (gpuContext is null)
        {
            glInterface.Dispose();
            if (ownsGlContext)
            {
                glContext.Dispose();
            }

            throw new InvalidOperationException(
                "Skia could not create a GPU context over this GL context "
                + $"(renderer '{glContext.Renderer}', version '{glContext.Version}').");
        }

        return new GpuSkiaSurfaceProvider(
            gpuContext,
            ownsContext: true,
            glInterface,
            ownsGlContext ? glContext : null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="RenderCaps.GpuBacked"/> is the flag the interop seam turns on: a drawable holding a
    /// <see cref="GlTextureImage"/> is legal on this provider's targets and on no other shipping
    /// configuration, and I.7 asks it to say so at attach rather than at draw. Reading it through
    /// <c>Env.Surfaces.Caps</c> is how a scene decides; see the class remarks for what a scene does
    /// after deciding, which is not the same question.
    /// </remarks>
    public RenderCaps Caps => RenderCaps.SkiaSurface | RenderCaps.GpuBacked;

    /// <summary>Gets the largest texture extent the device supports, in pixels.</summary>
    public int MaxTextureSize { get; }

    /// <summary>Gets the managed id of the thread this provider is bound to.</summary>
    public int OwnerThreadId => ownerThreadId;

    /// <summary>Gets the owned or borrowed Skia GPU context, for a host that drives its own frame loop.</summary>
    /// <remarks>
    /// Backend-facing. A host uses it to flush once per frame and to read cache statistics; engine
    /// code goes through this provider instead.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public GRContext NativeContext
    {
        get
        {
            ObjectDisposedException.ThrowIf(context is null, this);
            return context;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The surface is created at the provider's normalized sample count with a top-left origin and
    /// unknown pixel geometry, matching every other engine surface. Only a wrapped window
    /// framebuffer is bottom-left, and this provider does not create one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="spec"/> has a color-space tag with no GPU realization, or dimensions above
    /// <see cref="MaxTextureSize"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Skia declined to create the surface.</exception>
    public IRenderTarget CreateTarget(in SurfaceSpec spec)
    {
        EnsureUsable();
        var normalizedSpec = Normalize(spec);
        if (normalizedSpec.Width > MaxTextureSize || normalizedSpec.Height > MaxTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spec),
                $"A {normalizedSpec.Width}×{normalizedSpec.Height} surface exceeds the device's "
                + $"maximum texture size of {MaxTextureSize}.");
        }

        var colorType = RasterSkiaSurfaceProvider.ResolveColorType(normalizedSpec.ColorSpace, nameof(spec));
        var imageInfo = new SKImageInfo(
            normalizedSpec.Width,
            normalizedSpec.Height,
            colorType,
            SKAlphaType.Premul,
            ColorSpaceFor(normalizedSpec.ColorSpace));
        using var properties = new SKSurfaceProperties(SKPixelGeometry.Unknown);
        var surface = SKSurface.Create(
            context!,
            budgeted: false,
            imageInfo,
            normalizedSpec.SampleCount,
            GRSurfaceOrigin.TopLeft,
            properties,
            shouldCreateWithMips: false)
            ?? throw new InvalidOperationException(
                $"Skia failed to create a {normalizedSpec.Width}×{normalizedSpec.Height} GPU surface "
                + $"at {normalizedSpec.SampleCount} sample(s).");

        try
        {
            return new SkiaRenderTarget(surface, normalizedSpec, Caps);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The returned <see cref="SkiaCompositor"/> creates its layer targets and accumulation surface
    /// through this provider, so every surface a frame touches is normalized the same way and every
    /// specification-match predicate inside it compares like with like.
    /// </remarks>
    public ICompositor CreateCompositor()
    {
        EnsureUsable();
        return new SkiaCompositor(this);
    }

    /// <summary>Returns this provider's normalized form of a requested specification.</summary>
    /// <param name="spec">The requested specification.</param>
    /// <remarks>
    /// The sample count is clamped against the device maximum <em>for that specification's color
    /// type</em>, which is not one number: half-float surfaces are commonly single-sampled on
    /// hardware that multisamples 8-bit ones happily.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The color-space tag has no GPU realization.</exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public SurfaceSpec Normalize(in SurfaceSpec spec)
    {
        EnsureUsable();
        return spec.NormalizeForGpu(MaxSampleCountFor(spec.ColorSpace));
    }

    /// <summary>Gets the largest surface sample count the device supports for one color-space tag.</summary>
    /// <param name="colorSpace">The mandatory color-space tag.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tag has no GPU realization.</exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public int MaxSampleCountFor(CoreColorSpace colorSpace)
    {
        EnsureUsable();
        var colorType = RasterSkiaSurfaceProvider.ResolveColorType(colorSpace, nameof(colorSpace));
        var maximum = context!.GetMaxSurfaceSampleCount(colorType);
        return maximum < 1 ? 1 : maximum;
    }

    /// <summary>Wraps an externally owned GL texture as a draw-stable <see cref="IImage"/>.</summary>
    /// <param name="textureId">The non-zero GL name of a texture from this provider's GL context.</param>
    /// <param name="size">The texture's dimensions in pixels.</param>
    /// <returns>The cached wrap for this texture id.</returns>
    /// <remarks>See the overload taking options; this one wraps a plain premultiplied sRGB
    /// <c>GL_RGBA8</c> <c>GL_TEXTURE_2D</c> read top-left first.</remarks>
    public GlTextureImage WrapGlTexture(uint textureId, PixelSize size) =>
        WrapGlTexture(textureId, size, default);

    /// <summary>Wraps an externally owned GL texture as a draw-stable <see cref="IImage"/>.</summary>
    /// <param name="textureId">The non-zero GL name of a texture from this provider's GL context.</param>
    /// <param name="size">The texture's dimensions in pixels.</param>
    /// <param name="options">How to interpret the texture's storage, origin, and alpha.</param>
    /// <returns>
    /// The wrap for this texture id — the same instance on every call, rebuilt in place only when
    /// <paramref name="size"/> or <paramref name="options"/> says the texture was reallocated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>The three rules the author keeps.</strong> (1) The texture comes from
    /// <em>this provider's</em> GL context; share groups are not supported. (2) The author's GL work
    /// is complete before the wrap is sampled — Najm cannot see the author's command stream, so the
    /// fence or <c>glFlush</c> is theirs, as is <see cref="ResetGlState"/> after any raw GL. (3) The
    /// texture stays alive until the wrap is disposed and the release handshake has fired; see
    /// <see cref="GlTextureImage.TextureReleased"/>.
    /// </para>
    /// <para>
    /// Repeated calls for an unchanged texture are a dictionary hit and allocate nothing, which is
    /// what makes calling this from inside a render method legitimate.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="textureId"/> is zero, or <paramref name="size"/> is empty or exceeds
    /// <see cref="MaxTextureSize"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> is not a supported combination.</exception>
    /// <exception cref="InvalidOperationException">Skia declined to wrap the texture.</exception>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public GlTextureImage WrapGlTexture(uint textureId, PixelSize size, in GlTextureOptions options)
    {
        EnsureUsable();
        if (wraps.TryGetValue(textureId, out var cached))
        {
            if (cached.Describes(size, options))
            {
                return cached;
            }

            // Same id, different shape: the author reallocated the texture in place. Rebuild the one
            // wrap rather than handing out a second, so a reference the author kept stays correct.
            ValidateWrapRequest(textureId, size, options);
            cached.Rebuild(size, options);
            return cached;
        }

        ValidateWrapRequest(textureId, size, options);
        var created = new GlTextureImage(this, textureId, size, options);
        wraps.Add(textureId, created);
        return created;
    }

    /// <summary>Releases the cached wrap for one texture id, if there is one.</summary>
    /// <param name="textureId">The GL name whose wrap should be released.</param>
    /// <returns>Whether a wrap was released.</returns>
    /// <remarks>
    /// Call this before deleting a texture whose id will not come back — the handshake in
    /// <see cref="GlTextureImage.TextureReleased"/> tells you when the deletion is safe. A texture
    /// reallocated at the same id needs nothing: pass the new size to
    /// <see cref="WrapGlTexture(uint, PixelSize, in GlTextureOptions)"/> instead.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public bool ReleaseGlTexture(uint textureId)
    {
        EnsureUsable();
        if (!wraps.TryGetValue(textureId, out var wrap))
        {
            return false;
        }

        wrap.Dispose();
        return true;
    }

    /// <summary>Flushes recorded GPU work, and by default submits it to the driver.</summary>
    /// <param name="submit">Whether to submit the flushed work rather than only record the flush.</param>
    /// <remarks>
    /// A host calls this once per frame after rendering. It is also the second half of the texture
    /// release handshake: a disposed wrap's <see cref="GlTextureImage.TextureReleased"/> fires here,
    /// not at disposal, because that is when Skia has genuinely finished with the texture.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public void Flush(bool submit = true)
    {
        EnsureUsable();
        context!.Flush(submit);
    }

    /// <summary>
    /// Tells Skia that raw GL calls were issued behind its back and its cached GL state is stale.
    /// </summary>
    /// <remarks>
    /// Mandatory after an author's own GL work and before the next Skia draw. Skia caches what it
    /// believes the GL state machine holds — bound program, bound framebuffer, blend state — and
    /// skips redundant calls on that basis; an author's pipeline invalidates those assumptions, and
    /// the result of not saying so is corrupt drawing rather than an error.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public void ResetGlState()
    {
        EnsureUsable();
        context!.ResetContext();
    }

    /// <summary>Rebinds the provider to the calling thread after its GL context moved there.</summary>
    /// <remarks>
    /// Make the GL context current on the new thread <em>first</em>. This only updates the thread the
    /// provider will accept calls from; it cannot make a context current on your behalf, and calling
    /// it without having done so trades a loud failure for the silent one it exists to prevent.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public void Rebind()
    {
        ObjectDisposedException.ThrowIf(context is null, this);
        ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Releases every cached texture wrap, then the GPU context and GL context this provider owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Callers must dispose every created target and compositor before disposing the provider. Use
    /// of a retained target after provider disposal is outside the lifetime contract.
    /// </para>
    /// <para>
    /// The order is pinned: wraps, then a flush so their release handshakes fire, then the
    /// <see cref="GRContext"/>, then the GL context. Releasing a <see cref="GRContext"/> after its GL
    /// context has gone is the classic shutdown crash.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        var owned = context;
        if (owned is null)
        {
            return;
        }

        // Copied because disposing a wrap removes it from the dictionary.
        if (wraps.Count > 0)
        {
            foreach (var wrap in wraps.Values.ToArray())
            {
                wrap.Dispose();
            }

            wraps.Clear();
            if (!owned.IsAbandoned)
            {
                owned.Flush(submit: true);
            }
        }

        context = null;
        srgb.Dispose();
        linearSrgb.Dispose();
        if (ownsContext)
        {
            owned.Dispose();
        }

        ownedInterface?.Dispose();
        ownedGlContext?.Dispose();
    }

    /// <summary>Builds the native wrap for one texture, registering the release handshake.</summary>
    /// <remarks>
    /// The <see cref="GRBackendTexture"/> description is disposed immediately: Skia copies it into
    /// the image, so keeping it alive would pin a second handle to no purpose. What Skia does
    /// <em>not</em> copy is the texture — <c>FromTexture</c> borrows, which is the whole reason this
    /// class hands out a release callback instead of a deletion.
    /// </remarks>
    internal SKImage CreateNativeWrap(
        GlTextureImage owner,
        uint textureId,
        PixelSize size,
        in GlTextureOptions options)
    {
        EnsureUsable();
        var textureInfo = new GRGlTextureInfo(
            options.ResolvedTextureTarget,
            textureId,
            options.ResolvedSizedFormat);
        using var backendTexture = new GRBackendTexture(size.Width, size.Height, mipmapped: false, textureInfo);
        var image = SKImage.FromTexture(
            context!,
            backendTexture,
            options.Origin == GlTextureOrigin.BottomLeft
                ? GRSurfaceOrigin.BottomLeft
                : GRSurfaceOrigin.TopLeft,
            options.ResolveColorType(nameof(options)),
            options.IsStraightAlpha ? SKAlphaType.Unpremul : SKAlphaType.Premul,
            ColorSpaceFor(options.ColorSpace),
            GlTextureImage.ReleaseDelegate,
            owner);
        return image
            ?? throw new InvalidOperationException(
                $"Skia declined to wrap GL texture {textureId} as a {size.Width}×{size.Height} image; "
                + "the texture must exist in this provider's GL context and match the stated format.");
    }

    /// <summary>Drops one wrap from the cache as it disposes itself.</summary>
    internal void ForgetWrap(GlTextureImage wrap) => wraps.Remove(wrap.TextureId);

    /// <summary>Gets the provider-owned tagged color space for one portable tag.</summary>
    /// <remarks>
    /// Provider-owned and created once. Every surface and every wrap is tagged (§3.4: untagged
    /// surfaces do not exist), so building a fresh <see cref="SKColorSpace"/> per call would be an
    /// allocation on a path an author may take every frame.
    /// </remarks>
    internal SKColorSpace ColorSpaceFor(CoreColorSpace colorSpace) =>
        colorSpace == CoreColorSpace.LinearSrgb ? linearSrgb : srgb;

    private void ValidateWrapRequest(uint textureId, PixelSize size, in GlTextureOptions options)
    {
        if (textureId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textureId),
                "Zero is not a GL texture name; it unbinds the target instead of naming a texture.");
        }

        if (size.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "A wrapped texture must have positive dimensions.");
        }

        if (size.Width > MaxTextureSize || size.Height > MaxTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                $"A {size.Width}×{size.Height} texture exceeds the device's maximum texture size of "
                + $"{MaxTextureSize}.");
        }

        options.Validate(nameof(options));
    }

    /// <summary>Asserts the provider is alive and being called from the thread it is bound to.</summary>
    private void EnsureUsable([CallerMemberName] string? member = null)
    {
        ObjectDisposedException.ThrowIf(context is null, this);
        var callingThreadId = Environment.CurrentManagedThreadId;
        if (callingThreadId != ownerThreadId)
        {
            throw new InvalidOperationException(
                $"{nameof(GpuSkiaSurfaceProvider)}.{member} was called from thread {callingThreadId} "
                + $"but the provider is bound to thread {ownerThreadId}, which holds its GL context "
                + "current. GPU work issued from another thread produces transparent black with no "
                + "error; make the GL context current on this thread and call "
                + $"{nameof(Rebind)} if the move is deliberate.");
        }
    }
}
