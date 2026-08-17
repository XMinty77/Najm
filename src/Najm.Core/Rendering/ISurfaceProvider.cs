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

    /// <summary>
    /// Backend-facing SPI: creates an owned compositor that composites through this provider's
    /// surfaces.
    /// </summary>
    /// <remarks>
    /// This member is called by the engine, not by authors, and makes the provider the backend's
    /// surface-<em>and</em>-composition authority: one object decides how pixels are stored and how
    /// layers are combined. A compositor is per-scene and scene-lifetime — <see cref="Scene"/>
    /// acquires one when it loads and disposes it when it unloads — while the provider is
    /// environment-lifetime and outlives every compositor it creates. Like a target, the returned
    /// compositor is caller-owned and must be disposed before its provider.
    /// </remarks>
    ICompositor CreateCompositor();
}
