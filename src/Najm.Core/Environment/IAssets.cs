namespace Najm.Core;

/// <summary>Loads and caches shared resources behind backend-neutral handles.</summary>
/// <remarks>
/// <para>
/// <strong>Placeholder.</strong> This interface exists so <see cref="SceneEnvironment"/> can be a
/// closed set of five capabilities today. Its real surface — image, font-face, audio-clip, path,
/// and capability-gated shader handles, the caches behind them, and the rule that asset I/O is
/// confined to load and attach transitions — lands with the asset system, and nothing may depend on
/// the shape of this type until then.
/// </para>
/// <para>
/// Core owns only the portable handles; a backend keeps its native realizations in its own side
/// tables keyed by handle identity, so the same handle can be realized by several backends without
/// a mutable interior slot.
/// </para>
/// </remarks>
public interface IAssets
{
}
