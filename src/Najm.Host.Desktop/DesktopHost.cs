using System.Diagnostics;
using System.Numerics;
using Najm.Core;
using Najm.Skia;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using Key = Najm.Core.Key;

namespace Najm.Host.Desktop;

/// <summary>Runs a scene live in a window: the desktop composition root of ARCHITECTURE §4.6.</summary>
/// <remarks>
/// <para>
/// <strong>What a host is.</strong> §4.6: "A host is the composition root. It assembles the
/// environment, owns the clock and platform event pump, feeds ticks, provides render targets, and
/// delivers output." Everything in this class is one of those five things. It holds no scene logic,
/// no drawing, and no engine policy — the scene is a portable program and this is one platform it
/// can be run on.
/// </para>
/// <para>
/// <strong>The frame, in §4.7's order:</strong> pump events and map window to virtual coordinates;
/// advance the clock; tick; render; flush; clear the letterbox bars; swap. The one departure is
/// where the bars are cleared — after the render rather than before it — because §5.3's merge ends
/// in a replace-blit over the whole output and would otherwise erase them. Nothing draws between
/// the two steps and the two regions are disjoint, so the frame is the one §4.7 describes.
/// DEVIATIONS 33 has the long version.
/// </para>
/// <para>
/// <strong>One scaling point, both directions.</strong> §5.1 requires the host to letterbox
/// virtual→output and inverse-map input through the same transform. Both come from one
/// <see cref="Letterbox"/> value, re-resolved whenever the framebuffer changes size. Rendering goes
/// through it implicitly — <see cref="Scene.Render(IRenderTarget)"/> derives the same placement
/// from the same <see cref="FramePlacement"/> rule — and pointer coordinates go through it
/// explicitly, on their way into the <see cref="InputBuffer"/>.
/// </para>
/// <para>
/// <strong>Two windowing facts this host is built around, both learned the hard way on Linux.</strong>
/// It asks for <see cref="ContextAPI.OpenGLES"/> rather than desktop GL, because the desktop
/// core-profile window comes up with no stencil buffer on Mesa and Ganesh needs stencil for the
/// target it is handed. And it filters <c>egl*</c> and <c>glX*</c> out of the address loader it
/// gives Skia: GLFW builds its ES context through GLX here, libglvnd's <c>glXGetProcAddress</c>
/// returns a non-null dispatch stub for <em>any</em> name including <c>eglQueryString</c>, and
/// Skia's GLES probe calls that stub and dereferences what comes back. The symptom is a segmentation
/// fault inside libSkiaSharp during context creation, with no managed exception to catch.
/// </para>
/// <para>
/// <strong>Not thread-safe, and single-run at a time.</strong> §3.5 makes the engine
/// single-threaded; <see cref="Run(Func{Scene})"/> occupies the calling thread until the window
/// closes and refuses to be re-entered.
/// </para>
/// <example>
/// The whole of what an author writes to see their scene live:
/// <code>
/// new DesktopHost(new HostOptions { Title = "Hydrogen" })
///     .Run(() => new HydrogenScene(seed: 7));
/// </code>
/// </example>
/// </remarks>
public sealed class DesktopHost
{
    /// <summary>GL's name for the framebuffer currently bound to <c>GL_FRAMEBUFFER</c>.</summary>
    private const GetPName FramebufferBinding = (GetPName)0x8CA6;

    /// <summary>GL's name for the bound framebuffer's stencil depth.</summary>
    private const GetPName StencilBits = (GetPName)0x0D57;

    /// <summary>GL's name for the bound framebuffer's multisample count.</summary>
    private const GetPName Samples = (GetPName)0x80A9;

    private readonly HostOptions options;
    private Letterbox letterbox;
    private Vector2 devicePixelsPerWindowUnit = Vector2.One;
    private Scene? scene;
    private bool restartRequested;
    private bool framebufferChanged;
    private bool running;

    /// <summary>Creates a host over one set of options.</summary>
    /// <param name="options">
    /// The host's configuration. It is read at <see cref="Run(Func{Scene})"/> and not copied, so
    /// changing it between runs changes the next one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public DesktopHost(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    /// <summary>Runs scenes from a factory until the window closes, warm-restarting on demand.</summary>
    /// <param name="factory">
    /// Constructs the scene. It is called once at startup and again on every warm restart, so
    /// constructor arguments survive a restart and a scene instance is never reused — §4.1's
    /// single-driver rule.
    /// </param>
    /// <remarks>
    /// This is §4.6's <c>new DesktopHost(options).Run(() =&gt; new PhononScene(seed: 7))</c>, and it
    /// is the form to prefer: §15's manual warm restart reconstructs the scene from exactly this
    /// factory while the window, the GL context, the GPU provider, and their caches stay up.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// This host is already running, the factory returned null, or the platform could not give the
    /// window a GL context Skia can drive.
    /// </exception>
    public void Run(Func<Scene> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RunCore(factory, canRestart: true);
    }

    /// <summary>Runs one already-constructed scene until the window closes.</summary>
    /// <param name="scene">The scene to run. It is loaded, ticked, rendered, and unloaded once.</param>
    /// <remarks>
    /// §15: "<c>Run(sceneInstance)</c> remains single-run sugar and cannot warm-restart because no
    /// factory exists." The restart key is still reserved from the scene under this overload — a key
    /// that is the host's is the host's whether or not the host can act on it — and pressing it does
    /// nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="InvalidOperationException">This host is already running.</exception>
    public void Run(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var pending = scene;
        RunCore(
            () => Interlocked.Exchange(ref pending, null)
                ?? throw new InvalidOperationException(
                    "Run(Scene) has no factory and cannot construct a second scene. Use "
                    + "Run(Func<Scene>) if the scene should be restartable."),
            canRestart: false);
    }

    private void RunCore(Func<Scene> factory, bool canRestart)
    {
        if (running)
        {
            throw new InvalidOperationException(
                "This host is already running a scene. A host drives one scene at a time (ARCHITECTURE section 4.1).");
        }

        running = true;
        restartRequested = false;
        framebufferChanged = false;
        try
        {
            RunWindow(factory, canRestart);
        }
        finally
        {
            running = false;
            scene = null;
        }
    }

    private void RunWindow(Func<Scene> factory, bool canRestart)
    {
        var windowOptions = WindowOptions.Default with
        {
            Size = new Vector2D<int>(options.Width, options.Height),
            Title = options.Title,

            // OpenGL ES, not desktop GL: see the class remarks. 3.0 is what Ganesh's GLES backend
            // asks for and what every target platform can give.
            API = new GraphicsAPI(
                ContextAPI.OpenGLES,
                ContextProfile.Core,
                ContextFlags.Default,
                new APIVersion(3, 0)),

            // Stated rather than assumed. Skia's clip-and-cover work needs a stencil buffer on the
            // target it is handed, and a default framebuffer with none fails at draw time rather
            // than at creation.
            PreferredStencilBufferBits = 8,
            VSync = options.VSync,
            IsVisible = true,

            // The host owns presentation: it swaps after it has cleared the bars, not before.
            ShouldSwapAutomatically = false,
        };

        using var window = Window.Create(windowOptions);
        window.Initialize();

        var glContext = window.GLContext
            ?? throw new InvalidOperationException(
                "The window came up without a GL context, so there is nothing for Skia to render "
                + "through. This host requires an OpenGL ES 3.0 capable window.");
        var gl = GL.GetApi(window);

        GRGlInterface? glInterface = null;
        GRContext? gpuContext = null;
        GpuSkiaSurfaceProvider? provider = null;
        IRenderTarget? backbuffer = null;
        IInputContext? inputContext = null;
        WindowInput? windowInput = null;
        try
        {
            glInterface = GRGlInterface.CreateGles(name => LoadProcAddress(glContext, name));
            if (glInterface is null || !glInterface.Validate())
            {
                throw new InvalidOperationException(
                    "Skia could not build a valid GL ES interface over this window's context "
                    + $"(renderer '{gl.GetStringS(StringName.Renderer)}', version "
                    + $"'{gl.GetStringS(StringName.Version)}'). The host requires OpenGL ES 3.0 or "
                    + "a compatible profile.");
            }

            gpuContext = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException(
                    "Skia could not create a GPU context over this window's GL context "
                    + $"(renderer '{gl.GetStringS(StringName.Renderer)}').");

            // ownsContext: true hands the GRContext's lifetime to the provider, which releases it
            // in the pinned order — wraps, flush, GRContext — before this method releases the GL
            // interface and the window under it.
            provider = new GpuSkiaSurfaceProvider(gpuContext, ownsContext: true);
            gpuContext = null;

            var input = new InputBuffer();
            Reserve(input, options.OverlayKey);
            Reserve(input, options.RestartKey);

            scene = factory()
                ?? throw new InvalidOperationException("The scene factory returned null.");
            var environment = new SceneEnvironment(
                provider,
                options.Assets,
                options.Typesetter,
                options.Audio,
                provider.Caps);
            scene.Load(environment);

            RefreshPlacement(window);
            backbuffer = WrapBackbuffer(gl, provider, window);
            window.FramebufferResize += _ =>
            {
                // The placement updates here rather than at the top of the frame so that a pointer
                // event arriving later in the same pump is mapped through the new geometry. The
                // wrap itself is GPU work and waits for the frame.
                framebufferChanged = true;
                RefreshPlacement(window);
            };
            window.FocusChanged += focused =>
            {
                if (!focused)
                {
                    windowInput?.ReleaseHeldState();
                }
            };

            inputContext = window.CreateInput();
            windowInput = new WindowInput(inputContext, input, WindowPointToVirtual, OnReservedKey);

            var clock = new FrameClock(ClockPolicy.Live(options.MaxDt));
            var wall = Stopwatch.StartNew();
            var previous = wall.Elapsed.TotalSeconds;

            while (true)
            {
                input.BeginFrame();
                window.DoEvents();
                if (window.IsClosing)
                {
                    break;
                }

                if (framebufferChanged)
                {
                    framebufferChanged = false;
                    backbuffer.Dispose();
                    backbuffer = WrapBackbuffer(gl, provider, window);
                }

                if (restartRequested)
                {
                    restartRequested = false;
                    if (canRestart)
                    {
                        scene = Restart(scene, factory, environment);
                        clock = new FrameClock(ClockPolicy.Live(options.MaxDt));
                        RefreshPlacement(window);
                    }
                }

                var now = wall.Elapsed.TotalSeconds;
                var delta = now - previous;
                previous = now;

                scene.Tick(new TickContext(clock.Advance(delta), input.Block));
                scene.Render(backbuffer);
                provider.Flush(submit: true);
                ClearBars(gl, provider);
                window.SwapBuffers();
            }
        }
        finally
        {
            // The pinned teardown order of §4.6, outside in: the scene releases the compositor and
            // its layer targets, then the wrapped backbuffer, then the provider (which flushes and
            // releases the GRContext it owns), then Skia's GL interface, then the window and the GL
            // context under it. Releasing a GRContext after its GL context has gone is the classic
            // shutdown crash, and this is the order that cannot produce it.
            windowInput?.Dispose();
            inputContext?.Dispose();
            TearDownScene();
            backbuffer?.Dispose();
            provider?.Dispose();
            gpuContext?.Dispose();
            glInterface?.Dispose();
        }
    }

    /// <summary>Answers Skia's address queries, and refuses the two prefixes that crash it.</summary>
    /// <remarks>
    /// See the class remarks for why. Returning zero here is the truthful answer on a GLX-backed
    /// context — there is no EGL in the process — and it is the loader itself that has to say so,
    /// because libglvnd will happily hand back a stub for a name it has never heard of.
    /// </remarks>
    private static IntPtr LoadProcAddress(Silk.NET.Core.Contexts.IGLContext glContext, string name)
    {
        if (name.StartsWith("egl", StringComparison.Ordinal) ||
            name.StartsWith("glX", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        return glContext.TryGetProcAddress(name, out var address) ? address : IntPtr.Zero;
    }

    private static void Reserve(InputBuffer input, Key key)
    {
        // Key.Unknown is how an option says "do not reserve this at all": it is a defined value, so
        // reserving it would be legal and would silently swallow every key this host cannot map.
        if (key != Key.Unknown)
        {
            input.Reserve(key);
        }
    }

    /// <summary>Wraps the window's default framebuffer, reading its shape from GL rather than guessing.</summary>
    private static IRenderTarget WrapBackbuffer(GL gl, GpuSkiaSurfaceProvider provider, IWindow window)
    {
        gl.GetInteger(FramebufferBinding, out var framebuffer);
        gl.GetInteger(StencilBits, out var stencilBits);
        gl.GetInteger(Samples, out var samples);

        var size = window.FramebufferSize;
        return provider.WrapBackbuffer(
            new PixelSize(size.X, size.Y),
            samples,
            stencilBits,
            Core.ColorSpace.Srgb,
            (uint)framebuffer);
    }

    /// <summary>Re-resolves the one mapping rendering and pointer math both read.</summary>
    private void RefreshPlacement(IWindow window)
    {
        var framebuffer = window.FramebufferSize;
        var logical = window.Size;
        if (framebuffer.X <= 0 || framebuffer.Y <= 0)
        {
            // A minimized or zero-sized window has no placement to resolve. Keeping the last one is
            // better than throwing: the window will come back, and nothing is drawn meanwhile.
            return;
        }

        letterbox = Letterbox.Resolve(
            scene?.VirtualResolution ?? new Vector2(1920f, 1080f),
            new PixelSize(framebuffer.X, framebuffer.Y));

        // §3.3 calls window space "physical pixels", and the platform disagrees: pointer positions
        // arrive in the same logical units the window was asked for, which are not device pixels
        // under hi-DPI. This is the one conversion between them, and it is why §5.1 can say hi-DPI
        // "falls out for free" — the render scale already accounts for the larger framebuffer.
        devicePixelsPerWindowUnit = logical.X > 0 && logical.Y > 0
            ? new Vector2(framebuffer.X / (float)logical.X, framebuffer.Y / (float)logical.Y)
            : Vector2.One;
    }

    private Vector2 WindowPointToVirtual(Vector2 windowPoint) =>
        letterbox.ToVirtual(windowPoint * devicePixelsPerWindowUnit);

    private void OnReservedKey(Key key)
    {
        if (key == options.RestartKey)
        {
            restartRequested = true;
        }

        // options.OverlayKey is reserved and consumed with no effect: §15's debug overlay is not
        // built. Reserving it now means building it later does not take a key a scene had started
        // to use. HostOptions.OverlayKey documents that.
    }

    /// <summary>§15's warm restart: a fresh scene over the environment that is already up.</summary>
    private Scene Restart(Scene current, Func<Scene> factory, SceneEnvironment environment)
    {
        scene = null;
        try
        {
            current.Stop();
        }
        finally
        {
            current.Unload();
        }

        var replacement = factory()
            ?? throw new InvalidOperationException("The scene factory returned null on restart.");
        replacement.Load(environment);
        scene = replacement;
        return replacement;
    }

    private void TearDownScene()
    {
        var current = scene;
        if (current is null)
        {
            return;
        }

        scene = null;
        try
        {
            current.Stop();
        }
        finally
        {
            current.Unload();
        }
    }

    /// <summary>Paints <see cref="HostOptions.BarColor"/> over everything outside the content rect.</summary>
    /// <remarks>
    /// A scissored clear rather than a Skia pass: the bars are flat color over a rectangle, which is
    /// the one thing GL does without help. <see cref="GpuSkiaSurfaceProvider.ResetGlState"/> follows
    /// because Skia caches what it believes the GL state machine holds and this changed three pieces
    /// of it behind Skia's back — the same rule an author's own GL pipeline follows.
    /// </remarks>
    private void ClearBars(GL gl, GpuSkiaSurfaceProvider provider)
    {
        Span<Rect> bars = stackalloc Rect[2];
        var count = letterbox.GetBars(bars);
        if (count == 0)
        {
            return;
        }

        var color = options.BarColor;
        var height = letterbox.OutputSize.Height;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.ColorMask(true, true, true, true);
        gl.Enable(EnableCap.ScissorTest);
        gl.ClearColor(color.R, color.G, color.B, color.A);
        for (var index = 0; index < count; index++)
        {
            var bar = bars[index];
            var left = (int)MathF.Round(bar.Left);
            var top = (int)MathF.Round(bar.Top);
            var width = (int)MathF.Round(bar.Width);
            var barHeight = (int)MathF.Round(bar.Height);

            // GL's window origin is the bottom-left corner and the content rect's is the top-left,
            // which is the same flip the wrapped backbuffer's GRSurfaceOrigin carries.
            gl.Scissor(left, height - (top + barHeight), (uint)width, (uint)barHeight);
            gl.Clear(ClearBufferMask.ColorBufferBit);
        }

        gl.Disable(EnableCap.ScissorTest);
        provider.ResetGlState();
    }
}
