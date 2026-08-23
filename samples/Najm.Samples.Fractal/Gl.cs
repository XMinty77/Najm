using System.Runtime.InteropServices;
using System.Text;

namespace Najm.Samples.Fractal;

/// <summary>
/// The author-side GL ES binding. Najm issues no drawing GL of its own — <c>Najm.Skia</c> binds
/// <c>libGLESv2</c> for four <c>glGetString</c> calls and nothing else — so an author who renders
/// their own texture brings the entry points they need. In a windowed host this would be Silk.NET;
/// here it is hand-rolled, exactly as <c>tests/Najm.Skia.Tests/Gpu/TestGl.cs</c> does.
/// </summary>
/// <remarks>
/// Deliberately narrow: only the calls this sample's pipeline issues. See NOTES.md finding F-5.
/// </remarks>
internal static partial class Gl
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
    internal const int Linear = 0x2601;
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
    internal const uint MaxTextureSize = 0x0D33;

    [LibraryImport(Library)]
    internal static partial void glGenTextures(int count, [Out] uint[] textures);

    [LibraryImport(Library)]
    internal static partial void glDeleteTextures(int count, [In] uint[] textures);

    [LibraryImport(Library)]
    internal static partial void glBindTexture(uint target, uint texture);

    [LibraryImport(Library)]
    internal static partial void glTexImage2D(
        uint target,
        int level,
        int internalFormat,
        int width,
        int height,
        int border,
        uint format,
        uint type,
        IntPtr pixels);

    [LibraryImport(Library)]
    internal static partial void glTexParameteri(uint target, uint parameter, int value);

    [LibraryImport(Library)]
    internal static partial void glGenFramebuffers(int count, [Out] uint[] framebuffers);

    [LibraryImport(Library)]
    internal static partial void glDeleteFramebuffers(int count, [In] uint[] framebuffers);

    [LibraryImport(Library)]
    internal static partial void glBindFramebuffer(uint target, uint framebuffer);

    [LibraryImport(Library)]
    internal static partial void glFramebufferTexture2D(
        uint target,
        uint attachment,
        uint textureTarget,
        uint texture,
        int level);

    [LibraryImport(Library)]
    internal static partial uint glCheckFramebufferStatus(uint target);

    [LibraryImport(Library)]
    internal static partial void glViewport(int x, int y, int width, int height);

    [LibraryImport(Library)]
    internal static partial uint glCreateShader(uint type);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void glShaderSource(uint shader, int count, string[] source, int[]? length);

    [LibraryImport(Library)]
    internal static partial void glCompileShader(uint shader);

    [LibraryImport(Library)]
    internal static partial void glGetShaderiv(uint shader, uint parameter, [Out] int[] values);

    [LibraryImport(Library)]
    internal static partial void glGetShaderInfoLog(uint shader, int maxLength, int[]? length, [Out] byte[] log);

    [LibraryImport(Library)]
    internal static partial void glDeleteShader(uint shader);

    [LibraryImport(Library)]
    internal static partial uint glCreateProgram();

    [LibraryImport(Library)]
    internal static partial void glAttachShader(uint program, uint shader);

    [LibraryImport(Library)]
    internal static partial void glLinkProgram(uint program);

    [LibraryImport(Library)]
    internal static partial void glGetProgramiv(uint program, uint parameter, [Out] int[] values);

    [LibraryImport(Library)]
    internal static partial void glGetProgramInfoLog(uint program, int maxLength, int[]? length, [Out] byte[] log);

    [LibraryImport(Library)]
    internal static partial void glUseProgram(uint program);

    [LibraryImport(Library)]
    internal static partial void glDeleteProgram(uint program);

    [LibraryImport(Library)]
    internal static partial void glGetIntegerv(uint parameter, [Out] int[] values);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int glGetUniformLocation(uint program, string name);

    [LibraryImport(Library)]
    internal static partial void glUniform1f(int location, float x);

    [LibraryImport(Library)]
    internal static partial void glUniform1i(int location, int x);

    [LibraryImport(Library)]
    internal static partial void glUniform2f(int location, float x, float y);

    [LibraryImport(Library)]
    internal static partial void glUniform4f(int location, float x, float y, float z, float w);

    [LibraryImport(Library)]
    internal static partial void glGenVertexArrays(int count, [Out] uint[] arrays);

    [LibraryImport(Library)]
    internal static partial void glDeleteVertexArrays(int count, [In] uint[] arrays);

    [LibraryImport(Library)]
    internal static partial void glBindVertexArray(uint array);

    [LibraryImport(Library)]
    internal static partial void glDrawArrays(uint mode, int first, int count);

    [LibraryImport(Library)]
    internal static partial void glFinish();

    [LibraryImport(Library)]
    internal static partial uint glGetError();

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
            var log = ReadLog(shader, isProgram: false);
            glDeleteShader(shader);
            throw new InvalidOperationException($"GLSL ES compile failed:\n{Number(source)}\n{log}");
        }

        return shader;
    }

    /// <summary>Links a program from two stages, failing with the driver's log.</summary>
    internal static uint LinkProgram(string vertexSource, string fragmentSource)
    {
        var vertex = CompileShader(VertexShader, vertexSource);
        var fragment = CompileShader(FragmentShader, fragmentSource);
        var program = glCreateProgram();
        glAttachShader(program, vertex);
        glAttachShader(program, fragment);
        glLinkProgram(program);
        glDeleteShader(vertex);
        glDeleteShader(fragment);

        var status = new int[1];
        glGetProgramiv(program, LinkStatus, status);
        if (status[0] == 0)
        {
            var log = ReadLog(program, isProgram: true);
            glDeleteProgram(program);
            throw new InvalidOperationException($"GLSL ES link failed:\n{log}");
        }

        return program;
    }

    /// <summary>
    /// Resolves a uniform location and refuses to hand back <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// The single most important line in this file. <c>glUniform*(-1, ...)</c> is a specified
    /// <em>silent no-op</em>, so a uniform that the linker eliminated — because a shader edit made
    /// it unused — produces a plausible but wrong frame with no GL error and no log. Resolving
    /// through here turns that into a named exception at startup. See NOTES.md finding F-6.
    /// </remarks>
    internal static int RequireUniform(uint program, string name)
    {
        var location = glGetUniformLocation(program, name);
        if (location < 0)
        {
            throw new InvalidOperationException(
                $"Uniform '{name}' has no location in this program: it is either misspelled or was "
                + "eliminated by the linker for being unused. Setting it would have been a silent "
                + "no-op.");
        }

        return location;
    }

    /// <summary>Throws if the GL error flag is set, naming the stage that set it.</summary>
    internal static void ThrowOnError(string stage)
    {
        var error = glGetError();
        if (error != NoError)
        {
            throw new InvalidOperationException($"GL error 0x{error:X4} after {stage}.");
        }
    }

    private static string ReadLog(uint handle, bool isProgram)
    {
        var log = new byte[8192];
        if (isProgram)
        {
            glGetProgramInfoLog(handle, log.Length, null, log);
        }
        else
        {
            glGetShaderInfoLog(handle, log.Length, null, log);
        }

        return Encoding.ASCII.GetString(log).TrimEnd('\0', '\n');
    }

    /// <summary>Numbers a shader's lines so a driver log's line numbers point somewhere.</summary>
    private static string Number(string source)
    {
        var builder = new StringBuilder();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append((i + 1).ToString().PadLeft(4)).Append(" | ").AppendLine(lines[i].TrimEnd('\r'));
        }

        return builder.ToString();
    }
}
