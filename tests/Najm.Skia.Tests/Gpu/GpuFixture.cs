using Najm.Core;

namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// Brings up a headless GL context and a GPU provider for one test, or says out loud why it could
/// not.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One fixture per test, deliberately.</strong> A GL context is current on one thread at a
/// time, and the suite runs <c>[Parallelize(MethodLevel)]</c>. Sharing one context across tests
/// would leave it current on whichever thread created it and give every other test transparent black
/// with no error — the exact failure this seam is most likely to produce in the wild. Each test owns
/// a context on the thread it happens to run on, which is correct regardless of scheduling. The GPU
/// classes are additionally <c>[DoNotParallelize]</c> so a machine with a software rasterizer is not
/// asked to hold a dozen contexts at once.
/// </para>
/// <para>
/// <strong>Skipping is loud.</strong> An environment with no EGL reports the failing step and its
/// EGL error code through <see cref="Assert.Inconclusive(string)"/>. Setting
/// <c>NAJM_GPU_TESTS=required</c> turns that skip into a failure, so a CI lane that is supposed to
/// have a GPU stack cannot quietly stop testing one.
/// </para>
/// </remarks>
internal sealed class GpuFixture : IDisposable
{
    private const string RequirementVariable = "NAJM_GPU_TESTS";

    private GpuFixture(HeadlessGlContext glContext, GpuSkiaSurfaceProvider provider)
    {
        GlContext = glContext;
        Provider = provider;
    }

    /// <summary>Gets the headless context this fixture created and made current.</summary>
    internal HeadlessGlContext GlContext { get; }

    /// <summary>Gets the provider built over <see cref="GlContext"/>.</summary>
    internal GpuSkiaSurfaceProvider Provider { get; }

    /// <summary>Creates a fixture, or skips the calling test with the reason it could not.</summary>
    internal static GpuFixture Require()
    {
        if (!HeadlessGlContext.TryCreate(out var glContext, out var reason))
        {
            var message =
                "The headless GL stack is unavailable, so this GPU test did not run. " + reason;
            if (string.Equals(
                    Environment.GetEnvironmentVariable(RequirementVariable),
                    "required",
                    StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"{RequirementVariable}=required, but {char.ToLowerInvariant(message[4])}{message[5..]}");
            }

            Assert.Inconclusive(message);
            throw new InvalidOperationException("unreachable");
        }

        try
        {
            return new GpuFixture(glContext, GpuSkiaSurfaceProvider.CreateOver(glContext, ownsGlContext: false));
        }
        catch
        {
            glContext.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asserts a headless GL stack exists without holding one, for a test whose subject brings up
    /// its own context.
    /// </summary>
    /// <remarks>
    /// The offline GPU backend creates and owns its context, so a test of it must not be holding a
    /// second one on the same thread: two contexts made current in turn on one thread leaves the
    /// first silently non-current, which is precisely the failure mode this suite exists to avoid
    /// staging accidentally. So the probe context is created to prove EGL works and disposed
    /// immediately, and the subject is left to do its own bring-up.
    /// </remarks>
    internal static void RequireStack()
    {
        using var probe = Require();
    }

    /// <summary>Disposes the provider first and the GL context after, which is the pinned order.</summary>
    public void Dispose()
    {
        Provider.Dispose();
        GlContext.Dispose();
    }
}

/// <summary>Pixel readback and comparison helpers shared by the GPU tests.</summary>
internal static class GpuPixels
{
    /// <summary>Reads a target's pixels as tightly packed unpremultiplied RGBA bytes.</summary>
    internal static byte[] Read(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[checked(target.Size.Width * target.Size.Height * 4)];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    /// <summary>Returns the four bytes of one pixel, as a readable tuple.</summary>
    internal static (byte R, byte G, byte B, byte A) At(byte[] pixels, int width, int x, int y)
    {
        var index = ((y * width) + x) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    /// <summary>How two images of the same shape differ, channel by channel.</summary>
    /// <param name="MaxAbsolute">The largest single-channel difference anywhere.</param>
    /// <param name="MeanAbsolute">The mean single-channel difference over every channel of every pixel.</param>
    /// <param name="DifferingPixelFraction">The fraction of pixels differing in any channel at all.</param>
    /// <param name="BeyondToleranceFraction">The fraction of pixels differing by more than the stated tolerance.</param>
    internal readonly record struct Difference(
        int MaxAbsolute,
        double MeanAbsolute,
        double DifferingPixelFraction,
        double BeyondToleranceFraction)
    {
        /// <summary>Renders the difference for a failure message.</summary>
        internal string Describe() =>
            $"max |Δ| = {MaxAbsolute}, mean |Δ| = {MeanAbsolute:F3}, "
            + $"pixels differing at all = {DifferingPixelFraction:P2}, "
            + $"pixels beyond tolerance = {BeyondToleranceFraction:P3}";
    }

    /// <summary>Compares two same-shaped RGBA buffers.</summary>
    /// <param name="left">The first buffer.</param>
    /// <param name="right">The second buffer.</param>
    /// <param name="tolerance">The per-channel difference a pixel may show without being counted.</param>
    internal static Difference Compare(byte[] left, byte[] right, int tolerance)
    {
        Assert.HasCount(left.Length, right, "The two images must have the same shape to be compared.");
        var maxAbsolute = 0;
        var totalAbsolute = 0L;
        var differing = 0;
        var beyond = 0;
        for (var pixel = 0; pixel < left.Length; pixel += 4)
        {
            var pixelMax = 0;
            for (var channel = 0; channel < 4; channel++)
            {
                var delta = Math.Abs(left[pixel + channel] - right[pixel + channel]);
                totalAbsolute += delta;
                if (delta > pixelMax)
                {
                    pixelMax = delta;
                }
            }

            if (pixelMax > maxAbsolute)
            {
                maxAbsolute = pixelMax;
            }

            if (pixelMax > 0)
            {
                differing++;
            }

            if (pixelMax > tolerance)
            {
                beyond++;
            }
        }

        var pixelCount = left.Length / 4;
        return new Difference(
            maxAbsolute,
            totalAbsolute / (double)left.Length,
            differing / (double)pixelCount,
            beyond / (double)pixelCount);
    }
}
