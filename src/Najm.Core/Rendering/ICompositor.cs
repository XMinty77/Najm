using System.Numerics;

namespace Najm.Core;

/// <summary>Composites a scene's layer stack into one output target.</summary>
/// <remarks>
/// <para>
/// This type is backend-facing engine machinery, not an authoring API. A compositor is per-scene
/// and stateful: it owns the persistent per-layer targets and the accumulation surface, holds the
/// frame counters, and is disposed with the scene. <see cref="Scene.Load()"/> acquires one from the
/// environment's <see cref="ISurfaceProvider.CreateCompositor"/> and
/// <see cref="Scene.Render(IRenderTarget)"/> delegates to it.
/// </para>
/// <para>
/// The canonical algorithm is normative. Per visible layer, bottom to top in add order: bind the
/// layer's persistent target and clear it to <see cref="Layer.ClearColor"/>; install the base
/// transform and run the shared <see cref="RenderTraverser"/>; merge the layer target into the
/// accumulation surface with <see cref="Layer.Opacity"/> and <see cref="Layer.Blend"/>, placed 1:1
/// into <see cref="Layer.Viewport"/> when one is set. One final replace-blit moves the accumulation
/// surface onto the output, whose prior contents therefore never matter. A realization may take a
/// fast path only where it is byte-equivalent to that algorithm, and must be able to give it up
/// through <see cref="CompositorDebugOptions.ForceCanonicalPath"/>.
/// </para>
/// <para>
/// A compositor never calls <see cref="IRenderTarget.Snapshot"/>: every read of a surface still
/// being written this frame goes through a surface-to-surface draw, because snapshotting a surface
/// that is written afterwards forces a copy of its whole backing.
/// </para>
/// </remarks>
public interface ICompositor : IDisposable
{
    /// <summary>Gets this compositor's counters for the most recently completed render.</summary>
    CompositorStats Stats { get; }

    /// <summary>Gets the mutable debug hooks, read once at the start of each render.</summary>
    CompositorDebugOptions Debug { get; }

    /// <summary>Composites one layer stack into one output target.</summary>
    /// <param name="layers">The scene's layer stack, composited bottom layer first.</param>
    /// <param name="output">The write-only output target this frame lands in.</param>
    /// <param name="virtualResolution">
    /// The scene's finite, positive virtual resolution. A full-frame layer's target is sized
    /// <c>ceil(virtualResolution × renderScale)</c>; a viewport'd layer frames its own viewport
    /// instead.
    /// </param>
    /// <param name="renderScale">The finite, positive virtual-to-device pixel scale.</param>
    /// <remarks>
    /// The documented shape of this member carries no virtual resolution. Najm passes it because
    /// the compositor needs it in three places a backend cannot recover on its own: the traverser
    /// call, full-frame target sizing, and mapping a virtual-space viewport rect to device pixels.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layers"/> or <paramref name="output"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="virtualResolution"/> or <paramref name="renderScale"/> is not finite and
    /// positive.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The compositor has been disposed.</exception>
    void Render(
        LayerStack layers,
        IRenderTarget output,
        in Vector2 virtualResolution,
        float renderScale);
}
