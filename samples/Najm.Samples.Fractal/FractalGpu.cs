using Najm.Core;

namespace Najm.Samples.Fractal;

/// <summary>One frame's worth of shader state, in engine-side units.</summary>
/// <remarks>
/// The centre is a <see cref="double"/> pair here and a split float pair in the shader: the flight
/// path is computed in double because it is cheap to do so on the CPU, and only the split survives
/// the boundary. See <see cref="FractalGpu.Render"/>.
/// </remarks>
internal readonly record struct FractalUniforms
{
    /// <summary>Gets the real part of the complex centre.</summary>
    public required double CentreX { get; init; }

    /// <summary>Gets the imaginary part of the complex centre.</summary>
    public required double CentreY { get; init; }

    /// <summary>Gets the complex-plane half-height of the frame. Smaller is more zoomed in.</summary>
    public required double Scale { get; init; }

    /// <summary>Gets the frame rotation in radians, counter-clockwise.</summary>
    public required double Rotation { get; init; }

    /// <summary>Gets the fractional iteration limit.</summary>
    public required float MaxIterations { get; init; }

    /// <summary>Gets the palette phase, in ramp cycles.</summary>
    public required float PaletteShift { get; init; }

    /// <summary>Gets the ramp frequency, in cycles per unit of the square root of the iteration count.</summary>
    public float Bands { get; init; }

    /// <summary>
    /// Gets the smooth iteration count the ramp's zero sits at.
    /// </summary>
    /// <remarks>
    /// It has to track the depth. Nothing in the frame escapes in fewer iterations than the zoom
    /// level's own floor, so a ramp anchored at zero spends its whole first cycle on iteration
    /// counts that do not occur, and the visible field lands wherever that leaves it — which at
    /// depth was a wash of pale cream across a third of the frame.
    /// </remarks>
    public float NuFloor { get; init; }

    /// <summary>Gets the strength of the distance-estimated filament rim.</summary>
    public float RimGain { get; init; }

    /// <summary>Gets the strength of the moving iteration-limit wavefront.</summary>
    public float FrontGain { get; init; }

    /// <summary>Gets the linear-light exposure applied before tone mapping.</summary>
    public float Exposure { get; init; }
}

/// <summary>
/// The author-owned GL pipeline: one texture, one framebuffer object, one program, one fullscreen
/// triangle. Najm sees a texture id and a size and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Ownership.</strong> This type creates the texture and this type deletes it, exactly as
/// <see cref="Najm.Skia.GlTextureImage"/>'s remarks say. Najm's wrap borrows; disposing the wrap
/// leaves the texture intact. The order at shutdown is: release the wrap through the provider,
/// flush so the release handshake fires, then delete. <see cref="Dispose"/> does the deletion half;
/// the scene does the release half, because the scene is what holds the provider.
/// </para>
/// <para>
/// <strong>Thread affinity.</strong> Every method here requires the GL context current on the
/// calling thread. This sample is single-threaded and never moves it.
/// </para>
/// </remarks>
internal sealed class FractalGpu : IDisposable
{
    private readonly uint[] texture = new uint[1];
    private readonly uint[] framebuffer = new uint[1];
    private readonly uint[] vertexArray = new uint[1];
    private readonly uint program;
    private readonly int uResolution;
    private readonly int uCentreHi;
    private readonly int uCentreLo;
    private readonly int uScale;
    private readonly int uRotor;
    private readonly int uMaxIter;
    private readonly int uPaletteShift;
    private readonly int uBands;
    private readonly int uNuFloor;
    private readonly int uRimGain;
    private readonly int uFrontGain;
    private readonly int uExposure;
    private readonly int uSamples;
    private bool disposed;

    /// <summary>Compiles the program and allocates the render-to-texture target.</summary>
    /// <param name="size">The texture extent, which is also the shader's raster size.</param>
    /// <param name="samples">One, or four for rotated-grid supersampling inside the shader.</param>
    internal FractalGpu(PixelSize size, int samples)
    {
        Samples = samples;
        program = Gl.LinkProgram(FractalShader.Vertex, FractalShader.Fragment);

        // Every one of these throws rather than returning -1. A silently-dropped uniform is the
        // most plausible way for this shader to render something wrong but believable; see F-6.
        uResolution = Gl.RequireUniform(program, "uResolution");
        uCentreHi = Gl.RequireUniform(program, "uCentreHi");
        uCentreLo = Gl.RequireUniform(program, "uCentreLo");
        uScale = Gl.RequireUniform(program, "uScale");
        uRotor = Gl.RequireUniform(program, "uRotor");
        uMaxIter = Gl.RequireUniform(program, "uMaxIter");
        uPaletteShift = Gl.RequireUniform(program, "uPaletteShift");
        uBands = Gl.RequireUniform(program, "uBands");
        uNuFloor = Gl.RequireUniform(program, "uNuFloor");
        uRimGain = Gl.RequireUniform(program, "uRimGain");
        uFrontGain = Gl.RequireUniform(program, "uFrontGain");
        uExposure = Gl.RequireUniform(program, "uExposure");
        uSamples = Gl.RequireUniform(program, "uSamples");

        Gl.glGenTextures(1, texture);
        Allocate(size);

        Gl.glGenFramebuffers(1, framebuffer);
        Gl.glGenVertexArrays(1, vertexArray);
        Gl.ThrowOnError("pipeline construction");
    }

    /// <summary>Gets the GL name of the texture this pipeline owns.</summary>
    internal uint TextureId => texture[0];

    /// <summary>Gets the texture's current extent.</summary>
    internal PixelSize Size { get; private set; }

    /// <summary>Gets the in-shader supersampling factor, one or four.</summary>
    internal int Samples { get; }

    /// <summary>
    /// Reallocates the texture's storage at a new extent, keeping the same GL name.
    /// </summary>
    /// <remarks>
    /// The realistic resize: <c>glTexImage2D</c> again on the same texture, so the id Najm holds
    /// never changes and only the size does. Najm rebuilds its wrap in place when the next
    /// <c>WrapGlTexture</c> reports the new size.
    /// </remarks>
    internal void Reallocate(PixelSize size) => Allocate(size);

    /// <summary>Renders one frame into the texture and waits for it.</summary>
    /// <remarks>
    /// <para>
    /// The handoff obligation is the author's: Najm cannot see this command stream, so the wait is
    /// here. <c>glFinish</c> rather than a fence because on a software rasterizer the fence buys
    /// nothing and the simplest correct answer is worth more than the cycles.
    /// </para>
    /// <para>
    /// The framebuffer object is re-attached every frame. It is one call, it costs nothing next to
    /// the shader, and it means the pipeline is correct after a reallocation without a separate
    /// invalidation path.
    /// </para>
    /// </remarks>
    internal void Render(in FractalUniforms u)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        Gl.glBindFramebuffer(Gl.Framebuffer, framebuffer[0]);
        Gl.glFramebufferTexture2D(Gl.Framebuffer, Gl.ColorAttachment0, Gl.Texture2D, texture[0], 0);
        var status = Gl.glCheckFramebufferStatus(Gl.Framebuffer);
        if (status != Gl.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"The author's framebuffer is incomplete (0x{status:X4}) at {Size.Width}x{Size.Height}.");
        }

        Gl.glViewport(0, 0, Size.Width, Size.Height);
        Gl.glUseProgram(program);

        var (centreXHi, centreXLo) = Split(u.CentreX);
        var (centreYHi, centreYLo) = Split(u.CentreY);
        Gl.glUniform2f(uResolution, Size.Width, Size.Height);
        Gl.glUniform2f(uCentreHi, centreXHi, centreYHi);
        Gl.glUniform2f(uCentreLo, centreXLo, centreYLo);
        Gl.glUniform1f(uScale, (float)u.Scale);
        Gl.glUniform2f(uRotor, (float)Math.Cos(u.Rotation), (float)Math.Sin(u.Rotation));
        Gl.glUniform1f(uMaxIter, u.MaxIterations);
        Gl.glUniform1f(uPaletteShift, u.PaletteShift);
        Gl.glUniform1f(uBands, u.Bands);
        Gl.glUniform1f(uNuFloor, u.NuFloor);
        Gl.glUniform1f(uRimGain, u.RimGain);
        Gl.glUniform1f(uFrontGain, u.FrontGain);
        Gl.glUniform1f(uExposure, u.Exposure);
        Gl.glUniform1i(uSamples, Samples);

        Gl.glBindVertexArray(vertexArray[0]);
        Gl.glDrawArrays(Gl.Triangles, 0, 3);
        Gl.glFinish();
        Gl.ThrowOnError("fractal draw");

        Gl.glBindVertexArray(0);
        Gl.glUseProgram(0);
        Gl.glBindFramebuffer(Gl.Framebuffer, 0);
    }

    /// <summary>Deletes every GL object this pipeline created, the texture included.</summary>
    /// <remarks>
    /// Safe only after the provider has released its wrap and flushed. Deleting a texture Skia
    /// still holds is undefined behaviour that llvmpipe would merely draw black for.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Gl.glDeleteTextures(1, texture);
        Gl.glDeleteFramebuffers(1, framebuffer);
        Gl.glDeleteVertexArrays(1, vertexArray);
        Gl.glDeleteProgram(program);
    }

    /// <summary>
    /// Splits a double into two floats whose exact sum is the double to within float's own
    /// resolution of the remainder.
    /// </summary>
    /// <remarks>
    /// The shader adds the low half to the pixel offset before it adds the high half, which is what
    /// makes the sum worth taking: the two small quantities combine at their own magnitude instead
    /// of being rounded away against the large one.
    /// </remarks>
    private static (float Hi, float Lo) Split(double value)
    {
        var hi = (float)value;
        return (hi, (float)(value - hi));
    }

    private void Allocate(PixelSize size)
    {
        Size = size;
        Gl.glBindTexture(Gl.Texture2D, texture[0]);
        Gl.glTexImage2D(
            Gl.Texture2D,
            0,
            (int)Gl.Rgba8,
            size.Width,
            size.Height,
            0,
            Gl.Rgba,
            Gl.UnsignedByte,
            IntPtr.Zero);
        Gl.glTexParameteri(Gl.Texture2D, Gl.TextureMinFilter, Gl.Linear);
        Gl.glTexParameteri(Gl.Texture2D, Gl.TextureMagFilter, Gl.Linear);
        Gl.glTexParameteri(Gl.Texture2D, Gl.TextureWrapS, Gl.ClampToEdge);
        Gl.glTexParameteri(Gl.Texture2D, Gl.TextureWrapT, Gl.ClampToEdge);
        Gl.glBindTexture(Gl.Texture2D, 0);
        Gl.ThrowOnError($"texture allocation at {size.Width}x{size.Height}");
    }
}
