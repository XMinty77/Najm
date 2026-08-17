namespace Najm.Core;

/// <summary>Reports one <see cref="ICompositor"/>'s counters for a single completed render.</summary>
/// <remarks>
/// <para>
/// The counters are cheap value fields, cleared at the start of every render and published when it
/// completes, so they describe the frame that just ran rather than an accumulation over the
/// compositor's life. They exist to make composition costs and path choices observable — a fast
/// path that cannot be seen in the counters cannot be proven to have run — and are read by tests
/// and by a diagnostic overlay.
/// </para>
/// <para>
/// This type is backend-facing engine machinery, not an authoring API. Its M1 shape covers ordinary
/// layer composition only; isolation-bracket, backdrop, and pool counters arrive with the machinery
/// they measure.
/// </para>
/// </remarks>
public readonly record struct CompositorStats
{
    /// <summary>Gets how many persistent layer targets the compositor holds after this render.</summary>
    /// <remarks>
    /// A layer target is acquired on the layer's first composited render and kept until the layer
    /// leaves the stack or the compositor is disposed, so this counts held targets rather than
    /// layers drawn this frame: a layer that was skipped this frame still holds the target it
    /// acquired earlier.
    /// </remarks>
    public int LayerTargetCount { get; init; }

    /// <summary>Gets the estimated resident color-storage bytes of those layer targets.</summary>
    /// <remarks>
    /// The estimate is <c>width × height × bytesPerPixel × sampleCount</c> summed over the held
    /// targets. It is explicitly an estimate of color storage: mip, stencil, and other backend
    /// overhead is not included and is not knowable portably. The accumulation surface is not a
    /// layer target and is not counted here.
    /// </remarks>
    public long LayerTargetBytes { get; init; }

    /// <summary>Gets how many compositor-owned surfaces this render created.</summary>
    /// <remarks>
    /// Counts persistent layer targets and the accumulation surface alike, whether the acquisition
    /// was a first use or a re-acquisition forced by a size or specification change. A steady state
    /// — an unchanged layer stack rendered at an unchanged size and render scale — reports zero,
    /// which is what makes target reuse testable.
    /// </remarks>
    public int TargetAcquisitionCount { get; init; }

    /// <summary>Gets how many layer merges into the accumulation surface this render performed.</summary>
    /// <remarks>
    /// One merge per contributing layer. A skipped layer never merges, and the final replace-blit of
    /// the accumulation surface onto the output is not a merge. A render that took the single-layer
    /// fast path performs none.
    /// </remarks>
    public int MergeCount { get; init; }

    /// <summary>
    /// Gets whether this render took the FP-1 single-layer fast path straight to the output.
    /// </summary>
    /// <remarks>
    /// False whenever the canonical staged algorithm ran, including when
    /// <see cref="CompositorDebugOptions.ForceCanonicalPath"/> switched the fast path off. An
    /// equivalence test asserts both the byte-identity of the two paths and — through this counter —
    /// that the fast path genuinely ran in the unforced case.
    /// </remarks>
    public bool UsedSingleLayerFastPath { get; init; }
}
