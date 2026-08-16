namespace Najm.Core;

/// <summary>Represents a persistent drawable surface owned by its creator.</summary>
/// <remarks>
/// A target owns its draw context and returns the same reusable instance throughout its lifetime.
/// Each acquisition begins or resets a backend drawing pass, so unbalanced state from an earlier
/// acquisition cannot leak into the returned context. The context is borrowed and becomes invalid
/// when the target is disposed. Engine integrations with an explicit backend Begin/End seam must
/// end the pass in a <c>finally</c> block. A snapshot is a new caller-owned resource and must be
/// disposed independently. This interface is pre-release; its contract will be completed before
/// external package publication.
/// </remarks>
public interface IRenderTarget : IDisposable
{
    /// <summary>Gets the drawable content size.</summary>
    PixelSize Size { get; }

    /// <summary>Gets the provider-normalized surface specification.</summary>
    SurfaceSpec SurfaceSpec { get; }

    /// <summary>Begins a clean drawing pass and gets the target-owned reusable draw context.</summary>
    /// <remarks>
    /// Reacquisition returns the same object after restoring its backend baseline and discarding
    /// any unbalanced state left by the preceding acquisition. This overload is
    /// <see cref="GetContext(float)"/> at a render scale of one.
    /// </remarks>
    IDrawContext2D GetContext() => GetContext(1f);

    /// <summary>
    /// Begins a clean drawing pass at the given physical-pixel scale and gets the target-owned
    /// reusable draw context.
    /// </summary>
    /// <param name="renderScale">
    /// The finite positive device-pixel scale to install as the pass baseline. It is reported by
    /// <see cref="IDrawContext2D.RenderScale"/> and divided out of
    /// <see cref="IDrawContext2D.Scale"/>, so a unit-sized path covers
    /// <paramref name="renderScale"/> squared pixels.
    /// </param>
    /// <remarks>
    /// Reacquisition returns the same object after restoring its backend baseline and discarding
    /// any unbalanced state left by the preceding acquisition. The pass begins with the scale
    /// installed as the engine transform; a render traverser replaces that transform per node
    /// through <see cref="IDrawContext2D.SetEngineTransform"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="renderScale"/> is not finite or is not positive.
    /// </exception>
    IDrawContext2D GetContext(float renderScale);

    /// <summary>Creates an immutable caller-owned snapshot of the target's current content.</summary>
    IImage Snapshot();
}
