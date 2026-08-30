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
    /// <summary>Gets what every target this provider creates can do beyond portable 2D.</summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the capability question asked at attach time.</strong> The same flags reach a
    /// drawable through <see cref="IDrawContext2D.Caps"/>, but that is a render-time value: by then
    /// the node is attached, the frame is half built, and a scene that needed a GPU-backed target has
    /// nowhere left to go but a throw from inside <c>Render</c>, one frame after the decision was
    /// actually made. NAJM-SKIA I.7 asks for the check at attach for that reason, and attach sees the
    /// <see cref="SceneEnvironment"/> rather than a target — so without this member the check it
    /// specifies cannot be written at all. With it,
    /// <c>Env.Surfaces.Caps.HasFlag(RenderCaps.GpuBacked)</c> is answerable the moment a scene loads.
    /// </para>
    /// <para>
    /// It is provider-wide and constant. A provider realizes one backend, so every surface it hands
    /// out carries the same promise, and a caller may cache the answer for the environment's
    /// lifetime. A target the provider did <em>not</em> create — a vector writer, a host's wrapped
    /// window framebuffer — states its own capabilities and may state more; nothing from this
    /// provider ever states less.
    /// </para>
    /// <para>
    /// There is deliberately no default implementation, even though adding one would have spared
    /// existing implementers an edit. <see cref="RenderCaps.None"/> is the honest answer for a
    /// provider that promises nothing and a silent lie for a backend that simply forgot to say, and
    /// the two are indistinguishable at the point where it matters: content that needs a capability
    /// declines to attach, correctly and inexplicably. A compiler error is the cheaper failure.
    /// </para>
    /// </remarks>
    RenderCaps Caps { get; }

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
