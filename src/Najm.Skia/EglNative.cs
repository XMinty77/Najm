using System.Runtime.InteropServices;

namespace Najm.Skia;

/// <summary>
/// The EGL entry points <see cref="HeadlessGlContext"/> needs, bound directly to the system loader.
/// </summary>
/// <remarks>
/// <para>
/// Direct P/Invoke rather than a binding package on purpose: per NAJM-SKIA §16 the windowing and GL
/// stack belongs to the host, not to <c>Najm.Skia</c>, so this assembly must not take a dependency
/// on Silk.NET to bring a context up for offline rendering. The surface here is exactly the
/// surfaceless-context path and nothing more.
/// </para>
/// <para>
/// The library name is the Linux SONAME. Every member is reached through
/// <see cref="HeadlessGlContext"/>, which checks the platform before the first call so a non-Linux
/// process fails with a clear message instead of a loader exception from somewhere deep.
/// </para>
/// </remarks>
internal static partial class EglNative
{
    internal const string Library = "libEGL.so.1";

    /// <summary>The surfaceless platform token, <c>EGL_PLATFORM_SURFACELESS_MESA</c>.</summary>
    internal const uint PlatformSurfaceless = 0x31DD;

    /// <summary>The <c>EGL_OPENGL_ES_API</c> token for <c>eglBindAPI</c>.</summary>
    internal const uint OpenGlEsApi = 0x30A0;

    /// <summary><c>EGL_SUCCESS</c>, the error code a healthy call leaves behind.</summary>
    internal const int Success = 0x3000;

    /// <summary><c>EGL_SURFACE_TYPE</c>.</summary>
    internal const int SurfaceType = 0x3033;

    /// <summary><c>EGL_PBUFFER_BIT</c>.</summary>
    internal const int PbufferBit = 0x0001;

    /// <summary><c>EGL_RENDERABLE_TYPE</c>.</summary>
    internal const int RenderableType = 0x3040;

    /// <summary><c>EGL_OPENGL_ES3_BIT</c>.</summary>
    internal const int OpenGlEs3Bit = 0x0040;

    /// <summary><c>EGL_RED_SIZE</c>.</summary>
    internal const int RedSize = 0x3024;

    /// <summary><c>EGL_GREEN_SIZE</c>.</summary>
    internal const int GreenSize = 0x3023;

    /// <summary><c>EGL_BLUE_SIZE</c>.</summary>
    internal const int BlueSize = 0x3022;

    /// <summary><c>EGL_ALPHA_SIZE</c>.</summary>
    internal const int AlphaSize = 0x3021;

    /// <summary><c>EGL_NONE</c>, the attribute-list terminator.</summary>
    internal const int None = 0x3038;

    /// <summary><c>EGL_CONTEXT_CLIENT_VERSION</c>, also <c>EGL_CONTEXT_MAJOR_VERSION</c>.</summary>
    internal const int ContextClientVersion = 0x3098;

    /// <summary><c>EGL_WIDTH</c>.</summary>
    internal const int Width = 0x3057;

    /// <summary><c>EGL_HEIGHT</c>.</summary>
    internal const int Height = 0x3056;

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr eglGetProcAddress(string name);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglInitialize(IntPtr display, out int major, out int minor);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglBindAPI(uint api);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglChooseConfig(
        IntPtr display,
        [In] int[] attributes,
        [Out] IntPtr[] configs,
        int configSize,
        out int configCount);

    [LibraryImport(Library)]
    internal static partial IntPtr eglCreateContext(
        IntPtr display,
        IntPtr config,
        IntPtr shareContext,
        [In] int[] attributes);

    [LibraryImport(Library)]
    internal static partial IntPtr eglCreatePbufferSurface(
        IntPtr display,
        IntPtr config,
        [In] int[] attributes);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglDestroyContext(IntPtr display, IntPtr context);

    [LibraryImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool eglDestroySurface(IntPtr display, IntPtr surface);

    [LibraryImport(Library)]
    internal static partial IntPtr eglGetCurrentContext();

    [LibraryImport(Library)]
    internal static partial int eglGetError();

    /// <summary>
    /// <c>eglGetPlatformDisplayEXT</c>, resolved through <c>eglGetProcAddress</c> because it is an
    /// extension entry point rather than an exported symbol.
    /// </summary>
    /// <remarks>
    /// This is the one call that cannot be substituted. <c>eglGetDisplay(EGL_DEFAULT_DISPLAY)</c>
    /// returns a display that fails to initialize with <c>EGL_NOT_INITIALIZED</c> on a machine with
    /// no <c>/dev/dri</c> node; the platform entry point with
    /// <see cref="PlatformSurfaceless"/> is what brings up the software rasterizer.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr GetPlatformDisplayExt(uint platform, IntPtr nativeDisplay, int[]? attributes);
}

/// <summary>The handful of GL entry points this assembly reads for diagnostics.</summary>
/// <remarks>
/// Najm issues no drawing GL of its own — Skia owns every draw and the author owns their own
/// pipeline — so this binding stops at the strings <see cref="HeadlessGlContext"/> reports. Anything
/// larger belongs to the host's GL binding.
/// </remarks>
internal static partial class GlNative
{
    internal const string Library = "libGLESv2.so.2";

    /// <summary><c>GL_VENDOR</c>.</summary>
    internal const uint Vendor = 0x1F00;

    /// <summary><c>GL_RENDERER</c>.</summary>
    internal const uint Renderer = 0x1F01;

    /// <summary><c>GL_VERSION</c>.</summary>
    internal const uint Version = 0x1F02;

    /// <summary><c>GL_SHADING_LANGUAGE_VERSION</c>.</summary>
    internal const uint ShadingLanguageVersion = 0x8B8C;

    [LibraryImport(Library)]
    internal static partial IntPtr glGetString(uint name);
}
