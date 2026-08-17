namespace Najm.Core;

/// <summary>The asset store a <see cref="SceneEnvironment"/> uses when no host supplied one.</summary>
/// <remarks>
/// It holds nothing and loads nothing. A host that owns real assets constructs its own
/// <see cref="IAssets"/> — the backend does, natively — and passes it to the environment; this
/// stands in for offline loops and tests that never ask for an asset. When
/// <see cref="IAssets"/> grows its real surface, this becomes fail-loud in the manner of
/// <see cref="NullTypesetter"/>: there is no correct value to invent for an asset that was never
/// loaded.
/// </remarks>
public sealed class NullAssets : IAssets
{
    private NullAssets()
    {
    }

    /// <summary>Gets the shared instance; the type is stateless, so one is enough.</summary>
    public static NullAssets Instance { get; } = new();
}
