using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Najm.Skia;

/// <summary>
/// Owns a headless OpenGL ES context created through EGL's surfaceless platform, for GPU rendering
/// in a process with no window and no display server.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists, and where it stops.</strong> ARCHITECTURE §4.6 gives the GL context to
/// the <em>host</em>: a desktop host creates a window, brings up GL, and constructs
/// <see cref="GpuSkiaSurfaceProvider"/> over the context it already owns. Offline rendering has no
/// host and no window, and still needs a real context. This type is that context and nothing else —
/// it creates no Skia object, knows nothing about <see cref="Najm.Core.SurfaceSpec"/>, and is
/// deliberately separate from the provider so the provider stays constructible over a context it
/// did not create. That separation is not tidiness: it is the shape the real host needs.
/// </para>
/// <para>
/// <strong>Thread affinity.</strong> A GL context is current on one thread at a time, and every
/// consequence of getting that wrong is silent — Skia GPU draws against a context that is not
/// current produce transparent black with no exception. The context is made current on the thread
/// that creates it; <see cref="MakeCurrent"/> moves it to the calling thread and updates
/// <see cref="OwnerThreadId"/>. Two threads must never make the same context current at once.
/// </para>
/// <para>
/// <strong>Platform.</strong> Linux only, by construction: it binds <c>libEGL.so.1</c> and
/// <c>libGLESv2.so.2</c> directly rather than taking a binding-package dependency the architecture
/// assigns to the host. Every other platform fails loudly from
/// <see cref="Create"/>/<see cref="TryCreate"/> rather than at a loader boundary.
/// </para>
/// </remarks>
public sealed class HeadlessGlContext : IDisposable
{
    /// <summary>The pbuffer extent used when the driver refuses a surfaceless <c>eglMakeCurrent</c>.</summary>
    private const int FallbackPbufferExtent = 16;

    private readonly IntPtr display;
    private IntPtr context;
    private IntPtr surface;
    private bool disposed;

    private HeadlessGlContext(IntPtr display, IntPtr context, IntPtr surface)
    {
        this.display = display;
        this.context = context;
        this.surface = surface;
        OwnerThreadId = Environment.CurrentManagedThreadId;
        Vendor = ReadGlString(GlNative.Vendor);
        Renderer = ReadGlString(GlNative.Renderer);
        Version = ReadGlString(GlNative.Version);
        ShadingLanguageVersion = ReadGlString(GlNative.ShadingLanguageVersion);
    }

    /// <summary>Gets <c>GL_VENDOR</c> as reported by the live context.</summary>
    public string Vendor { get; }

    /// <summary>Gets <c>GL_RENDERER</c> as reported by the live context.</summary>
    public string Renderer { get; }

    /// <summary>Gets <c>GL_VERSION</c> as reported by the live context.</summary>
    public string Version { get; }

    /// <summary>Gets <c>GL_SHADING_LANGUAGE_VERSION</c> as reported by the live context.</summary>
    public string ShadingLanguageVersion { get; }

    /// <summary>Gets the managed id of the thread the context was last made current on.</summary>
    public int OwnerThreadId { get; private set; }

    /// <summary>Gets whether this context is the one current on the calling thread.</summary>
    /// <remarks>
    /// The authoritative check, asked of EGL rather than inferred from
    /// <see cref="OwnerThreadId"/>, so it stays honest if something outside this type changed the
    /// binding.
    /// </remarks>
    public bool IsCurrent => !disposed && EglNative.eglGetCurrentContext() == context;

    /// <summary>Whether this process can host a headless GL context at all.</summary>
    /// <remarks>
    /// A platform check only, and therefore cheap and side-effect free: it says whether
    /// <see cref="TryCreate"/> is worth calling, not whether it will succeed. A caller that needs
    /// the real answer calls <see cref="TryCreate"/> and reads its reason.
    /// </remarks>
    public static bool IsSupportedPlatform => OperatingSystem.IsLinux();

    /// <summary>Creates a surfaceless context and makes it current on the calling thread.</summary>
    /// <exception cref="PlatformNotSupportedException">The process is not running on Linux.</exception>
    /// <exception cref="InvalidOperationException">
    /// EGL is present but could not produce a current context; the message names the failing step
    /// and the EGL error code.
    /// </exception>
    public static HeadlessGlContext Create()
    {
        if (!IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException(
                "A headless GL context is available on Linux only, through EGL's surfaceless "
                + "platform. On other platforms the host owns the GL context and constructs "
                + $"{nameof(GpuSkiaSurfaceProvider)} over it.");
        }

        return TryCreate(out var created, out var reason)
            ? created
            : throw new InvalidOperationException(reason);
    }

    /// <summary>
    /// Attempts to create a surfaceless context, reporting why rather than throwing when EGL cannot
    /// supply one.
    /// </summary>
    /// <param name="context">The current context on success; otherwise null.</param>
    /// <param name="unavailableReason">
    /// On failure, a message naming the step that failed and the EGL error code behind it. This is
    /// what a test skips <em>loudly</em> with, so an environment that quietly lost its GL stack does
    /// not turn a GPU suite into a suite that silently never runs.
    /// </param>
    /// <returns>Whether a current context was created.</returns>
    public static bool TryCreate(
        [NotNullWhen(true)] out HeadlessGlContext? context,
        [NotNullWhen(false)] out string? unavailableReason)
    {
        context = null;
        if (!IsSupportedPlatform)
        {
            unavailableReason =
                "EGL headless contexts require Linux; this process is running on "
                + $"{RuntimeInformation.OSDescription}.";
            return false;
        }

        try
        {
            return TryCreateCore(out context, out unavailableReason);
        }
        catch (DllNotFoundException exception)
        {
            unavailableReason =
                $"The EGL/GLES runtime is not installed: {exception.Message} "
                + $"({EglNative.Library} and {GlNative.Library} must be loadable).";
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            unavailableReason = $"The installed EGL runtime is missing an entry point: {exception.Message}";
            return false;
        }
    }

    /// <summary>Makes this context current on the calling thread.</summary>
    /// <remarks>
    /// Moving a context between threads is legal only while it is current on no other thread. This
    /// method cannot detect that for you; it is here so a caller that parks work on a different
    /// thread can rebind deliberately rather than discover the silent-blank failure mode.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    /// <exception cref="InvalidOperationException"><c>eglMakeCurrent</c> failed.</exception>
    public void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!EglNative.eglMakeCurrent(display, surface, surface, context))
        {
            throw new InvalidOperationException(
                $"eglMakeCurrent failed while binding the headless context to thread "
                + $"{Environment.CurrentManagedThreadId} (EGL error 0x{EglNative.eglGetError():X4}).");
        }

        OwnerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Resolves a GL or EGL entry point by name.</summary>
    /// <param name="name">The entry-point name.</param>
    /// <returns>The address, or <see cref="IntPtr.Zero"/> when the driver does not export it.</returns>
    /// <remarks>
    /// This is the loader Skia's <c>GRGlInterface</c> is built over, and the reason the provider
    /// needs no GL binding of its own.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    public IntPtr GetProcAddress(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ObjectDisposedException.ThrowIf(disposed, this);
        return EglNative.eglGetProcAddress(name);
    }

    /// <summary>Releases the context and any fallback pbuffer surface.</summary>
    /// <remarks>
    /// <para>
    /// The context is unbound before it is destroyed, so a caller that disposes while it is current
    /// leaves the thread with no current context rather than with a dangling one.
    /// </para>
    /// <para>
    /// <strong>The display is deliberately not terminated.</strong> <c>eglGetPlatformDisplayEXT</c>
    /// returns the same <c>EGLDisplay</c> for the same platform for the whole process, so
    /// <c>eglTerminate</c> here would tear down every other live context in the process — including
    /// ones owned by unrelated code. The display costs nothing to leave initialized and is reclaimed
    /// with the process.
    /// </para>
    /// <para>
    /// Anything Skia holds on this context — a <c>GRContext</c>, its surfaces — must be disposed
    /// <em>before</em> this. Disposing a <c>GRContext</c> after its GL context is gone is the
    /// classic shutdown crash.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        EglNative.eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (context != IntPtr.Zero)
        {
            EglNative.eglDestroyContext(display, context);
            context = IntPtr.Zero;
        }

        if (surface != IntPtr.Zero)
        {
            EglNative.eglDestroySurface(display, surface);
            surface = IntPtr.Zero;
        }
    }

    private static bool TryCreateCore(
        [NotNullWhen(true)] out HeadlessGlContext? created,
        [NotNullWhen(false)] out string? unavailableReason)
    {
        created = null;
        var platformDisplayAddress = EglNative.eglGetProcAddress("eglGetPlatformDisplayEXT");
        if (platformDisplayAddress == IntPtr.Zero)
        {
            unavailableReason =
                "This EGL implementation does not export eglGetPlatformDisplayEXT, which is the only "
                + "way to reach the surfaceless platform; eglGetDisplay(EGL_DEFAULT_DISPLAY) is not a "
                + "substitute on a machine with no DRM device.";
            return false;
        }

        var getPlatformDisplay = Marshal.GetDelegateForFunctionPointer<EglNative.GetPlatformDisplayExt>(
            platformDisplayAddress);
        var display = getPlatformDisplay(EglNative.PlatformSurfaceless, IntPtr.Zero, null);
        if (display == IntPtr.Zero)
        {
            unavailableReason = Failure("eglGetPlatformDisplayEXT(EGL_PLATFORM_SURFACELESS_MESA)");
            return false;
        }

        if (!EglNative.eglInitialize(display, out _, out _))
        {
            unavailableReason = Failure("eglInitialize");
            return false;
        }

        if (!EglNative.eglBindAPI(EglNative.OpenGlEsApi))
        {
            unavailableReason = Failure("eglBindAPI(EGL_OPENGL_ES_API)");
            return false;
        }

        // No depth or stencil is requested. Skia renders into framebuffer objects it allocates and
        // attaches itself, so the config's own buffers are never the ones a draw uses; asking for
        // them would only narrow the set of configs that can satisfy the request.
        int[] configAttributes =
        [
            EglNative.SurfaceType, EglNative.PbufferBit,
            EglNative.RenderableType, EglNative.OpenGlEs3Bit,
            EglNative.RedSize, 8,
            EglNative.GreenSize, 8,
            EglNative.BlueSize, 8,
            EglNative.AlphaSize, 8,
            EglNative.None,
        ];
        var configs = new IntPtr[1];
        if (!EglNative.eglChooseConfig(display, configAttributes, configs, 1, out var configCount) ||
            configCount == 0)
        {
            unavailableReason = Failure("eglChooseConfig (no ES3-renderable RGBA8 config)");
            return false;
        }

        int[] contextAttributes = [EglNative.ContextClientVersion, 3, EglNative.None];
        var context = EglNative.eglCreateContext(display, configs[0], IntPtr.Zero, contextAttributes);
        if (context == IntPtr.Zero)
        {
            unavailableReason = Failure("eglCreateContext (OpenGL ES 3)");
            return false;
        }

        // EGL_KHR_surfaceless_context is the fast road and is what Mesa offers here; a driver
        // without it still binds a context against a throwaway pbuffer, which costs one 16x16
        // surface and is never drawn into.
        var surface = IntPtr.Zero;
        if (!EglNative.eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context))
        {
            int[] pbufferAttributes =
            [
                EglNative.Width, FallbackPbufferExtent,
                EglNative.Height, FallbackPbufferExtent,
                EglNative.None,
            ];
            surface = EglNative.eglCreatePbufferSurface(display, configs[0], pbufferAttributes);
            if (surface == IntPtr.Zero || !EglNative.eglMakeCurrent(display, surface, surface, context))
            {
                unavailableReason = Failure("eglMakeCurrent (surfaceless, then over a pbuffer)");
                if (surface != IntPtr.Zero)
                {
                    EglNative.eglDestroySurface(display, surface);
                }

                EglNative.eglDestroyContext(display, context);
                return false;
            }
        }

        created = new HeadlessGlContext(display, context, surface);
        unavailableReason = null;
        return true;
    }

    private static string Failure(string step) =>
        $"{step} failed while creating a headless GL context (EGL error 0x{EglNative.eglGetError():X4}).";

    private static string ReadGlString(uint name) =>
        Marshal.PtrToStringAnsi(GlNative.glGetString(name)) ?? "(unavailable)";
}
