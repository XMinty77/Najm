using Najm.Core;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// Stands in for the author's own renderer: a GLSL ES program that draws a quadrant pattern into a
/// texture the author allocates, owns, and deletes.
/// </summary>
/// <remarks>
/// <para>
/// This is the hydrogen-orbital case in miniature. Najm never sees the shader source, never
/// allocates the texture, and never deletes it; all it is handed is a texture id and a size. The
/// pattern is chosen so that both <em>content</em> and <em>orientation</em> are decidable from a
/// handful of pixels: in GL's own coordinates the bottom half is red then green, the top half is
/// blue then white, and a disc of opaque black sits in the middle so the texture is genuinely
/// shaded rather than cleared.
/// </para>
/// <para>
/// Row zero of the texture is therefore the <em>bottom</em> row of what the shader drew, which is
/// what makes <see cref="GlTextureOrigin"/> observable rather than a matter of taste.
/// </para>
/// </remarks>
internal sealed class AuthorGlPipeline : IDisposable
{
    private const string VertexSource =
        """
        #version 300 es
        void main()
        {
            vec2 corners[3] = vec2[3](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            gl_Position = vec4(corners[gl_VertexID], 0.0, 1.0);
        }
        """;

    private const string FragmentSource =
        """
        #version 300 es
        precision highp float;
        uniform vec2 uSize;
        out vec4 fragColor;
        void main()
        {
            vec2 uv = gl_FragCoord.xy / uSize;
            if (distance(uv, vec2(0.5)) < 0.25)
            {
                fragColor = vec4(0.0, 0.0, 0.0, 1.0);
            }
            else if (uv.y < 0.5)
            {
                fragColor = uv.x < 0.5 ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0, 1.0, 0.0, 1.0);
            }
            else
            {
                fragColor = uv.x < 0.5 ? vec4(0.0, 0.0, 1.0, 1.0) : vec4(1.0, 1.0, 1.0, 1.0);
            }
        }
        """;

    private readonly uint[] texture = new uint[1];
    private readonly uint[] framebuffer = new uint[1];
    private readonly uint[] vertexArray = new uint[1];
    private readonly uint program;
    private readonly int sizeUniform;
    private bool disposed;

    internal AuthorGlPipeline(int width, int height)
    {
        TestGl.glGenTextures(1, texture);
        Allocate(width, height);

        TestGl.glGenFramebuffers(1, framebuffer);
        TestGl.glGenVertexArrays(1, vertexArray);

        program = TestGl.glCreateProgram();
        var vertex = TestGl.CompileShader(TestGl.VertexShader, VertexSource);
        var fragment = TestGl.CompileShader(TestGl.FragmentShader, FragmentSource);
        TestGl.glAttachShader(program, vertex);
        TestGl.glAttachShader(program, fragment);
        TestGl.glLinkProgram(program);
        var status = new int[1];
        TestGl.glGetProgramiv(program, TestGl.LinkStatus, status);
        if (status[0] == 0)
        {
            throw new InvalidOperationException($"GLSL ES link failed: {TestGl.ReadProgramLog(program)}");
        }

        TestGl.glDeleteShader(vertex);
        TestGl.glDeleteShader(fragment);
        sizeUniform = TestGl.glGetUniformLocation(program, "uSize");
    }

    /// <summary>Gets the GL name of the texture the author owns.</summary>
    internal uint TextureId => texture[0];

    /// <summary>Gets the texture's current dimensions.</summary>
    internal PixelSize Size { get; private set; }

    /// <summary>Gets whether the texture still exists as far as GL is concerned.</summary>
    internal bool TextureExists => TestGl.glIsTexture(texture[0]) != 0;

    /// <summary>Reallocates the texture's storage at a new size, keeping the same GL name.</summary>
    /// <remarks>
    /// The realistic reallocation: a resize calls <c>glTexImage2D</c> again on the same texture, so
    /// the id an author hands Najm does not change and only the size does.
    /// </remarks>
    internal void Reallocate(int width, int height) => Allocate(width, height);

    /// <summary>Renders the pattern into the texture and waits for it, as the documented handoff.</summary>
    /// <remarks>
    /// <c>glFinish</c> rather than a fence because a test wants the simplest correct answer. The
    /// obligation is the author's either way: Najm cannot see this command stream.
    /// </remarks>
    internal void Render()
    {
        TestGl.glBindFramebuffer(TestGl.Framebuffer, framebuffer[0]);
        TestGl.glFramebufferTexture2D(
            TestGl.Framebuffer,
            TestGl.ColorAttachment0,
            TestGl.Texture2D,
            texture[0],
            0);
        var status = TestGl.glCheckFramebufferStatus(TestGl.Framebuffer);
        Assert.AreEqual(
            TestGl.FramebufferComplete,
            status,
            $"The author's framebuffer is incomplete (0x{status:X4}).");

        TestGl.glViewport(0, 0, Size.Width, Size.Height);
        TestGl.glUseProgram(program);
        TestGl.glUniform2f(sizeUniform, Size.Width, Size.Height);
        TestGl.glBindVertexArray(vertexArray[0]);
        TestGl.glDrawArrays(TestGl.Triangles, 0, 3);
        TestGl.glFinish();
        Assert.AreEqual(TestGl.NoError, TestGl.glGetError(), "The author's GL pipeline reported an error.");

        TestGl.glBindVertexArray(0);
        TestGl.glBindFramebuffer(TestGl.Framebuffer, 0);
    }

    /// <summary>Deletes the texture, as the author must once Skia has released it.</summary>
    internal void DeleteTexture()
    {
        if (texture[0] != 0)
        {
            TestGl.glDeleteTextures(1, texture);
            texture[0] = 0;
        }
    }

    /// <summary>Releases every GL object this pipeline created.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DeleteTexture();
        TestGl.glDeleteFramebuffers(1, framebuffer);
        TestGl.glDeleteVertexArrays(1, vertexArray);
        TestGl.glDeleteProgram(program);
    }

    private void Allocate(int width, int height)
    {
        Size = new PixelSize(width, height);
        TestGl.glBindTexture(TestGl.Texture2D, texture[0]);
        TestGl.glTexImage2D(
            TestGl.Texture2D,
            0,
            (int)TestGl.Rgba8,
            width,
            height,
            0,
            TestGl.Rgba,
            TestGl.UnsignedByte,
            IntPtr.Zero);
        TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureMinFilter, TestGl.Nearest);
        TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureMagFilter, TestGl.Nearest);
        TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureWrapS, TestGl.ClampToEdge);
        TestGl.glTexParameteri(TestGl.Texture2D, TestGl.TextureWrapT, TestGl.ClampToEdge);
        TestGl.glBindTexture(TestGl.Texture2D, 0);
    }
}
