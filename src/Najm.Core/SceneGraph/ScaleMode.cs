namespace Najm.Core;

/// <summary>Controls how a 2D transform is resolved against a camera during rendering.</summary>
/// <remarks>
/// This setting never changes logical local or world matrices. Camera-aware
/// consumers apply <see cref="Virtual"/> only when resolving local space into
/// virtual presentation space.
/// </remarks>
public enum ScaleMode
{
    /// <summary>Inherit the complete logical ancestor and camera scale.</summary>
    Inherit,

    /// <summary>Resolve one local unit as one virtual unit while preserving rotation and translation.</summary>
    Virtual,
}

