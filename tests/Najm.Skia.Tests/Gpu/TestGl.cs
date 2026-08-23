using System.Runtime.InteropServices;
using System.Text;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// The author-side GL binding. It lives in the test assembly on purpose: Najm.Skia issues no drawing
/// GL of its own, so the calls an external pipeline needs are exactly the ones an author brings —
/// here by hand, in a real host through Silk.NET.
/// </summary>
internal static class TestGl
{
    private const string Library = "libGLESv2.so.2";

    internal const uint Texture2D = 0x0DE1;
    internal const uint Rgba8 = 0x8058;
    internal const uint Rgba = 0x1908;
    internal const uint UnsignedByte = 0x1401;
    internal const uint TextureMinFilter = 0x2801;
    internal const uint TextureMagFilter = 0x2800;
    internal const uint TextureWrapS = 0x2802;
    internal const uint TextureWrapT = 0x2803;
    internal const int Nearest = 0x2600;
    internal const int ClampToEdge = 0x812F;
    internal const uint Framebuffer = 0x8D40;
    internal const uint ColorAttachment0 = 0x8CE0;
    internal const uint FramebufferComplete = 0x8CD5;
    internal const uint VertexShader = 0x8B31;
    internal const uint FragmentShader = 0x8B30;
    internal const uint CompileStatus = 0x8B81;
    internal const uint LinkStatus = 0x8B82;
    internal const uint Triangles = 0x0004;
    internal const uint NoError = 0x0000;

    [DllImport(Library)]
    internal static extern void glGenTextures(int count, uint[] textures);

    [DllImport(Library)]
    internal static extern void glDeleteTextures(int count, uint[] textures);

    [DllImport(Library)]
    internal static extern void glBindTexture(uint target, uint texture);

    [DllImport(Library)]
    internal static extern void glTexImage2D(
        uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);

    [DllImport(Library)]
    internal static extern void glTexParameteri(uint target, uint parameter, int value);

    [DllImport(Library)]
    internal static extern void glGenFramebuffers(int count, uint[] framebuffers);

    [DllImport(Library)]
    internal static extern void glDeleteFramebuffers(int count, uint[] framebuffers);

    [DllImport(Library)]
    internal static extern void glBindFramebuffer(uint target, uint framebuffer);

    [DllImport(Library)]
    internal static extern void glFramebufferTexture2D(
        uint target, uint attachment, uint textureTarget, uint texture, int level);

    [DllImport(Library)]
    internal static extern uint glCheckFramebufferStatus(uint target);

    [DllImport(Library)]
    internal static extern void glViewport(int x, int y, int width, int height);

    [DllImport(Library)]
    internal static extern uint glCreateShader(uint type);

    [DllImport(Library)]
    internal static extern void glShaderSource(uint shader, int count, string[] source, int[]? length);

    [DllImport(Library)]
    internal static extern void glCompileShader(uint shader);

    [DllImport(Library)]
    internal static extern void glGetShaderiv(uint shader, uint parameter, int[] values);

    [DllImport(Library)]
    internal static extern void glGetShaderInfoLog(uint shader, int maxLength, int[]? length, byte[] log);

    [DllImport(Library)]
    internal static extern void glDeleteShader(uint shader);

    [DllImport(Library)]
    internal static extern uint glCreateProgram();

    [DllImport(Library)]
    internal static extern void glAttachShader(uint program, uint shader);

    [DllImport(Library)]
    internal static extern void glLinkProgram(uint program);

    [DllImport(Library)]
    internal static extern void glGetProgramiv(uint program, uint parameter, int[] values);

    [DllImport(Library)]
    internal static extern void glGetProgramInfoLog(uint program, int maxLength, int[]? length, byte[] log);

    [DllImport(Library)]
    internal static extern void glUseProgram(uint program);

    [DllImport(Library)]
    internal static extern void glDeleteProgram(uint program);

    [DllImport(Library)]
    internal static extern int glGetUniformLocation(uint program, string name);

    [DllImport(Library)]
    internal static extern void glUniform2f(int location, float x, float y);

    [DllImport(Library)]
    internal static extern void glGenVertexArrays(int count, uint[] arrays);

    [DllImport(Library)]
    internal static extern void glDeleteVertexArrays(int count, uint[] arrays);

    [DllImport(Library)]
    internal static extern void glBindVertexArray(uint array);

    [DllImport(Library)]
    internal static extern void glDrawArrays(uint mode, int first, int count);

    [DllImport(Library)]
    internal static extern void glFinish();

    [DllImport(Library)]
    internal static extern uint glGetError();

    [DllImport(Library)]
    internal static extern int glIsTexture(uint texture);

    /// <summary>Compiles one shader stage, failing with the driver's log rather than a status code.</summary>
    internal static uint CompileShader(uint type, string source)
    {
        var shader = glCreateShader(type);
        glShaderSource(shader, 1, [source], null);
        glCompileShader(shader);
        var status = new int[1];
        glGetShaderiv(shader, CompileStatus, status);
        if (status[0] == 0)
        {
            throw new InvalidOperationException($"GLSL ES compile failed: {ReadShaderLog(shader)}");
        }

        return shader;
    }

    private static string ReadShaderLog(uint shader)
    {
        var log = new byte[4096];
        glGetShaderInfoLog(shader, log.Length, null, log);
        return Encoding.ASCII.GetString(log).TrimEnd('\0', '\n');
    }

    /// <summary>Reads a program's link log, for a link failure message with the driver's own words.</summary>
    internal static string ReadProgramLog(uint program)
    {
        var log = new byte[4096];
        glGetProgramInfoLog(program, log.Length, null, log);
        return Encoding.ASCII.GetString(log).TrimEnd('\0', '\n');
    }
}
