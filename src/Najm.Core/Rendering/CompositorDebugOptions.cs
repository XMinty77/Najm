namespace Najm.Core;

/// <summary>Holds the debug hooks one <see cref="ICompositor"/> honors.</summary>
/// <remarks>
/// A compositor owns one instance for its lifetime and reads every option once at the start of a
/// render, so a frame is coherent even if an option changes while it is in flight. The options
/// exist to make fast paths testable rather than to configure rendering: a fast path that cannot be
/// switched off cannot be proven equivalent to the path it replaces.
/// </remarks>
public sealed class CompositorDebugOptions
{
    /// <summary>
    /// Gets or sets whether every fast path is disabled and the canonical staged algorithm runs
    /// instead. The default is false.
    /// </summary>
    /// <remarks>
    /// The two settings must produce byte-identical output for any scene that qualifies for a fast
    /// path; that equivalence is a test obligation of every realization.
    /// </remarks>
    public bool ForceCanonicalPath { get; set; }
}
