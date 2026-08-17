using System.Numerics;

namespace Najm.Core;

/// <summary>Runs a scene deterministically at a fixed step and delivers every frame to a sink.</summary>
/// <remarks>
/// <para>
/// This is the offline driver: it owns the loop, not the backend. Surfaces come from an injected
/// <see cref="ISurfaceProvider"/> — the CPU-raster Skia provider in practice — so the same loop
/// serves any backend and Core keeps no rendering dependency.
/// </para>
/// <para>
/// The provider is the only capability an offline run needs, so the loop builds the scene's
/// <see cref="SceneEnvironment"/> around it and lets Core's null objects supply the rest. That is
/// the honest description of a deterministic export: it loads no assets it was not handed, it draws
/// no text — <see cref="NullTypesetter"/> would say so loudly if a scene tried — and its audio is a
/// cue list somebody else assembles, not sound.
/// </para>
/// <para>
/// <strong>The timing contract.</strong> The clock is <see cref="ClockPolicy.Fixed(double)"/> at
/// the requested rate, so tick <c>k</c> carries <c>Dt = 1/fps</c> and <c>Elapsed = (k+1)/fps</c>,
/// derived rather than accumulated. <strong>Output frame <c>k</c> is the render performed after
/// tick <c>k</c></strong>, and frame indices start at zero. Every tick receives
/// <see cref="InputBlock.Empty"/> by contract: a deterministic run has no input, which is what makes
/// two fresh-instance runs of the same scene produce identical frames.
/// </para>
/// <para>
/// <strong>A still at time <c>t</c></strong> — see <see cref="RenderStill"/> — runs
/// <c>ceil(t × fps)</c> ticks and then renders once. At <c>t = 0</c> that is <em>zero</em> ticks, so
/// the exported frame is the loaded state and <c>OnStart</c>, which runs inside the first tick, does
/// not run at all.
/// </para>
/// </remarks>
public static class OfflineRenderer
{
    /// <summary>Renders a fixed-step sequence and submits every frame to the configured sink.</summary>
    /// <param name="scene">
    /// The scene instance to run. It is loaded, ticked, rendered, and unloaded here; a caller that
    /// wants determinism supplies a freshly constructed instance.
    /// </param>
    /// <param name="surfaces">The backend surface and composition authority the scene renders through.</param>
    /// <param name="options">The rate, length, output size, and sink for this run.</param>
    /// <returns>The number of frames submitted, which equals the number of ticks run.</returns>
    /// <remarks>
    /// <para>
    /// The length is <see cref="OfflineOptions.Frames"/> when set, otherwise
    /// <c>ceil(Duration × Fps)</c>; a frame count wins over a duration. A zero-length run is legal
    /// and produces an empty but properly opened and closed stream.
    /// </para>
    /// <para>
    /// The scene is unloaded before this method returns, on every path. If the run fails — a faulting
    /// scene, a sink that throws — the original failure propagates and the scene is still unloaded;
    /// a sink that also implements <see cref="IDisposable"/> is disposed so an external encoder does
    /// not survive the abandoned run. Failures from that cleanup are suppressed rather than allowed
    /// to mask the real one.
    /// </para>
    /// <para>
    /// Nothing is buffered: one frame is in flight at a time, in a pooled
    /// <see cref="PixelFrameLease"/>, and the warm loop allocates no managed memory per frame.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The options specify neither a frame count nor a duration, or the scene cannot be loaded.
    /// </exception>
    public static long Render(Scene scene, ISurfaceProvider surfaces, OfflineOptions options)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(options);

        var sink = options.Sink;
        var frameCount = options.ResolveFrameCount();
        var framesPerSecond = options.Fps;

        scene.Load(new SceneEnvironment(surfaces));
        try
        {
            var size = ResolveOutputSize(scene.VirtualResolution, options.Scale, options.OutputSize);
            using var target = surfaces.CreateTarget(
                new SurfaceSpec(size.Width, size.Height, options.SampleCount, options.ColorSpace));

            sink.Begin(new FrameStreamInfo(
                size.Width,
                size.Height,
                framesPerSecond,
                options.Format,
                frameCount));

            var clock = new FrameClock(ClockPolicy.Fixed(framesPerSecond));
            for (var frame = 0L; frame < frameCount; frame++)
            {
                scene.Tick(new TickContext(clock.Advance(), InputBlock.Empty));
                scene.Render(target);
                Capture(target, sink, frame, size, options.Format);
            }

            sink.End();
        }
        catch
        {
            AbandonQuietly(scene, sink);
            throw;
        }

        scene.Unload();
        return frameCount;
    }

    /// <summary>Renders exactly one frame at a chosen simulated time and submits it.</summary>
    /// <param name="scene">The scene instance to evaluate; loaded, ticked, rendered, and unloaded here.</param>
    /// <param name="surfaces">The backend surface and composition authority the scene renders through.</param>
    /// <param name="sink">The one-frame stream's sink.</param>
    /// <param name="at">The finite, non-negative simulated time to seek to, in seconds.</param>
    /// <param name="framesPerSecond">The fixed rate the seek is quantized against.</param>
    /// <param name="scale">The virtual-to-output pixel scale.</param>
    /// <param name="format">The pixel layout submitted to the sink.</param>
    /// <param name="sampleCount">The requested surface sample count.</param>
    /// <param name="colorSpace">The output surface's color-space tag.</param>
    /// <returns>The number of ticks run before the render, which is <c>ceil(at × fps)</c>.</returns>
    /// <remarks>
    /// <para>
    /// The seek is <c>ceil(at × fps)</c> ticks followed by a single render, so the frame is the one
    /// the sequence loop would emit as output frame <c>ceil(at × fps) − 1</c>. At <c>at: 0</c> the
    /// tick count is zero: the loaded state is rendered and <c>OnStart</c> never runs, because it
    /// runs inside the first tick.
    /// </para>
    /// <para>
    /// A still is a one-frame stream, so the submitted frame index is 0 regardless of how many ticks
    /// preceded it, and the stream's <see cref="FrameStreamInfo.FrameCount"/> is 1.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="at"/> is not finite and non-negative, or a rate, scale, or sample count is
    /// invalid.
    /// </exception>
    public static long RenderStill(
        Scene scene,
        ISurfaceProvider surfaces,
        IFrameSink sink,
        double at,
        double framesPerSecond = 60d,
        float scale = 1f,
        PixelFormat format = PixelFormat.Rgba8888,
        int sampleCount = 1,
        ColorSpace colorSpace = ColorSpace.Srgb)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The requested pixel format is not defined.", nameof(format));
        }
        if (!Enum.IsDefined(colorSpace))
        {
            throw new ArgumentException("The color-space tag is not defined.", nameof(colorSpace));
        }

        var ticks = FixedStepTiming.TicksForStill(at, framesPerSecond);

        scene.Load(new SceneEnvironment(surfaces));
        try
        {
            var size = ResolveOutputSize(scene.VirtualResolution, scale, outputSize: null);
            using var target = surfaces.CreateTarget(
                new SurfaceSpec(size.Width, size.Height, sampleCount, colorSpace));

            sink.Begin(new FrameStreamInfo(size.Width, size.Height, framesPerSecond, format, frameCount: 1L));

            var clock = new FrameClock(ClockPolicy.Fixed(framesPerSecond));
            for (var tick = 0L; tick < ticks; tick++)
            {
                scene.Tick(new TickContext(clock.Advance(), InputBlock.Empty));
            }

            scene.Render(target);
            Capture(target, sink, frame: 0L, size, format);
            sink.End();
        }
        catch
        {
            AbandonQuietly(scene, sink);
            throw;
        }

        scene.Unload();
        return ticks;
    }

    /// <summary>
    /// Resolves the output pixel size: an explicit size when one is given, otherwise the virtual
    /// resolution scaled and rounded to nearest, never below one pixel on either axis.
    /// </summary>
    /// <remarks>
    /// Rounding rather than truncating or taking a ceiling is what keeps <c>1920 × 0.5</c> at
    /// exactly 960 instead of drifting by a pixel on whichever axis the float product lands just
    /// outside an integer.
    /// </remarks>
    private static PixelSize ResolveOutputSize(Vector2 virtualResolution, float scale, PixelSize? outputSize)
    {
        if (outputSize is { } explicitSize)
        {
            if (explicitSize.IsEmpty)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputSize),
                    explicitSize,
                    "An explicit offline output size must have positive dimensions.");
            }

            return explicitSize;
        }

        var width = ScaleAxis(virtualResolution.X, scale);
        var height = ScaleAxis(virtualResolution.Y, scale);
        return new PixelSize(width, height);
    }

    private static int ScaleAxis(float virtualExtent, float scale)
    {
        var scaled = Math.Round((double)virtualExtent * scale, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaled) || scaled > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                scale,
                $"A {virtualExtent} virtual extent at scale {scale} is not a representable pixel count.");
        }

        return Math.Max(1, (int)scaled);
    }

    /// <summary>
    /// Copies the freshly rendered target into a pooled lease and transfers it to the sink.
    /// </summary>
    /// <remarks>
    /// The snapshot never crosses the submission boundary: it is read into the lease and released
    /// here, so the sink can hold the frame while the surface is overwritten next tick. If the copy
    /// fails the lease is returned to the pool; once <see cref="IFrameSink.Submit"/> is entered the
    /// sink owns it and this method never touches it again.
    /// </remarks>
    private static void Capture(
        IRenderTarget target,
        IFrameSink sink,
        long frame,
        PixelSize size,
        PixelFormat format)
    {
        var lease = PixelFrameLease.Rent(size.Width, size.Height, format);
        try
        {
            using var snapshot = target.Snapshot();
            snapshot.CopyPixels(lease.Pixels, format);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        sink.Submit(frame, lease);
    }

    /// <summary>
    /// Tears down an abandoned run without masking the failure that abandoned it.
    /// </summary>
    /// <remarks>
    /// The sink never gets its <see cref="IFrameSink.End"/> — the stream is not finished and calling
    /// it would report a truncated result as a completed one — but a sink holding an external
    /// process or file handle is disposed so nothing outlives the run.
    /// </remarks>
    private static void AbandonQuietly(Scene scene, IFrameSink sink)
    {
        if (sink is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Suppressed: the render's own failure is the one worth reporting.
            }
        }

        try
        {
            scene.Unload();
        }
        catch
        {
            // Suppressed for the same reason.
        }
    }
}
