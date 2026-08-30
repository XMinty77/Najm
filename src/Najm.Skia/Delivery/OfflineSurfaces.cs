using Najm.Core;

namespace Najm.Skia;

/// <summary>Builds the surface provider one <see cref="OfflineBackend"/> names.</summary>
/// <remarks>
/// The whole point of the type is that its callers never see the assembly, and in particular never
/// see the ordering. Bringing up the GPU path is a GL context and a Skia context over it, and they
/// have to be released in the opposite order — a <c>GRContext</c> disposed after the GL context it
/// was built on is the classic shutdown crash. <c>ownsGlContext: true</c> hands both the ownership
/// and the ordering to the provider, so every caller's teardown is one <c>using</c> and there is
/// nothing left to get right.
/// </remarks>
internal static class OfflineSurfaces
{
    /// <summary>Creates a provider realizing <paramref name="backend"/>, owning everything under it.</summary>
    /// <param name="backend">The backend to assemble.</param>
    /// <returns>A provider the caller disposes, and nothing else the caller must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="backend"/> is not a defined backend.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// <see cref="OfflineBackend.Gpu"/> was asked for off Linux.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// EGL could not produce a current context, or Skia could not build a GPU context over it. The
    /// message names the failing step.
    /// </exception>
    internal static ISurfaceProvider Create(OfflineBackend backend)
    {
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentException("The requested offline backend is not defined.", nameof(backend));
        }

        if (backend == OfflineBackend.Raster)
        {
            return new RasterSkiaSurfaceProvider();
        }

        // Create is deliberately not wrapped in a try/catch that disposes the context: every failure
        // path inside CreateOver already disposes it, precisely because it was handed ownership.
        var glContext = HeadlessGlContext.Create();
        return GpuSkiaSurfaceProvider.CreateOver(glContext, ownsGlContext: true);
    }
}
