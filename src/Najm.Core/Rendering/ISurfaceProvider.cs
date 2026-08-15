namespace Najm.Core;

/// <summary>Creates render targets for one backend environment.</summary>
/// <remarks>
/// Providers are environment-lifetime resources. A created target is caller-owned and must be
/// disposed before its provider. Providers and targets are single-threaded unless a realization
/// explicitly documents otherwise. This interface is pre-release; its contract will be completed
/// before external package publication.
/// </remarks>
public interface ISurfaceProvider : IDisposable
{
    /// <summary>Creates an owned target using the provider's normalized form of the specification.</summary>
    /// <param name="spec">The requested dimensions, sample count, and mandatory color-space tag.</param>
    IRenderTarget CreateTarget(in SurfaceSpec spec);
}
