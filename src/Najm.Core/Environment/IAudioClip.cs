namespace Najm.Core;

/// <summary>A portable handle to one loaded sound, as an <see cref="IAudioSink"/> receives it.</summary>
/// <remarks>
/// <strong>Placeholder.</strong> Clip handles come from <see cref="IAssets"/> like any other asset,
/// and this type exists only so <see cref="IAudioSink.Play"/> can carry the shape the audio model
/// specifies without inventing an audio subsystem first. Its real surface arrives with the asset
/// system.
/// </remarks>
public interface IAudioClip
{
}
