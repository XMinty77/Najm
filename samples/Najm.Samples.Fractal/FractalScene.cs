using System.Numerics;
using Najm.Core;
using Najm.Skia;

namespace Najm.Samples.Fractal;

/// <summary>
/// A Mandelbrot flight: the author's GLSL ES shader renders the set into a texture this scene owns,
/// and the engine composites that texture, a vignette, and a small instrument as one frame.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The scene owns the GL pipeline.</strong> It is created in <see cref="OnLoad"/>, where the
/// environment first exists, and torn down in <see cref="OnUnload"/>, before the environment goes.
/// That is the only window in which the GL context is guaranteed current and the provider alive.
/// </para>
/// <para>
/// <strong>The GL work happens in <see cref="Update"/>, not in a node's <c>Render</c>.</strong>
/// Skia's GPU backend records draws and submits them later, so raw GL issued in the middle of a
/// recording executes in an order that has nothing to do with the order it was written in. Doing the
/// author's rendering in the tick puts it unambiguously before anything Skia records for the frame,
/// and leaves <c>Render</c> to do nothing but hand over an image.
/// </para>
/// <para>
/// <strong>The frame is rendered once in <see cref="OnLoad"/> as well.</strong> A scene exported at
/// <c>at: 0</c> runs zero ticks by contract, so a scene whose content is produced in the tick
/// exports empty there. Priming at load costs one shader pass and means the scene is never in a
/// state where its texture holds nothing.
/// </para>
/// </remarks>
internal sealed class FractalScene : Scene
{
    private readonly IFlight flight;
    private readonly int samples;
    private FractalTexture? texture;
    private FractalGpu? pipeline;
    private FractalUniforms current;

    /// <summary>Creates a scene that follows <paramref name="flight"/> at the given sample count.</summary>
    /// <param name="flight">The camera move, or a fixed frame while tuning.</param>
    /// <param name="samples">One, or four for rotated-grid supersampling inside the shader.</param>
    public FractalScene(IFlight flight, int samples)
    {
        this.flight = flight;
        this.samples = samples;
        VirtualResolution = new Vector2(Design.Frame.Width, Design.Frame.Height);
    }

    /// <inheritdoc />
    protected override void OnLoad()
    {
        // Finding F-4: the seam's operations live on the concrete provider, and SceneEnvironment
        // hands out the interface. Every GL-interop scene starts with this cast.
        var gpu = Env.Surfaces as GpuSkiaSurfaceProvider
            ?? throw new InvalidOperationException(
                $"This scene renders through a GL texture and needs {nameof(GpuSkiaSurfaceProvider)}; "
                + $"it was loaded with {Env.Surfaces.GetType().Name}.");

        pipeline = new FractalGpu(Design.Frame, samples);

        // Constructing the pipeline compiled a program and allocated a texture behind Skia's back.
        // Say so before Skia draws anything again.
        gpu.ResetGlState();

        texture = new FractalTexture(gpu, pipeline);
        current = flight.At(0d);
        texture.Advance(current);

        var screen = Layers.Add(new ScreenLayer { ClearColor = Design.Background });
        var root = screen.Root;
        root.Add(new FractalNode(texture) { ZIndex = 0 });
        root.Add(new VignetteNode { ZIndex = 1 });
        root.Add(new InstrumentNode(Reading) { ZIndex = 2 });
    }

    /// <inheritdoc />
    protected override void Update(in TickContext tick)
    {
        // Output frame k is the render after tick k, and tick k carries Elapsed = (k+1)/fps. The
        // clip's own zero is therefore one step behind the clock's.
        current = flight.At(tick.Time.Elapsed - tick.Time.Dt);
        texture!.Advance(current);
    }

    /// <inheritdoc />
    protected override void OnUnload()
    {
        texture?.Dispose();
        texture = null;
        pipeline = null;
    }

    /// <summary>Converts the live uniforms into the instrument's already-normalized terms.</summary>
    private InstrumentReading Reading()
    {
        const int Decades = 5;
        var magnification = 1.30d / Math.Max(current.Scale, 1e-12d);
        return new InstrumentReading
        {
            Decades = Decades,
            DepthFraction = (float)(Math.Log10(Math.Max(magnification, 1d)) / Decades),
            IterationFraction = (float)((Math.Log10(Math.Max(current.MaxIterations, 10f)) - 1d) / 2.4d),
        };
    }
}
