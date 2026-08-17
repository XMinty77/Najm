namespace Najm.Core;

/// <summary>Realizes the audio a scene emits.</summary>
/// <remarks>
/// <para>
/// <strong>Placeholder.</strong> This interface exists so <see cref="SceneEnvironment"/> can be a
/// closed set of five capabilities today, and it carries exactly one member — enough to exist, and
/// enough for <see cref="NullAudioSink"/> to absorb. The real surface — the device realization, the
/// deterministic cue recorder, and the tee and gain decorators that compose them — lands with the
/// audio slice. Nothing may depend on the shape of this type until then.
/// </para>
/// <para>
/// Scenes emit audio, hosts realize it: an emission is data, exactly as a draw call is. A
/// deterministic offline run therefore does not produce sound at all — it produces a cue list a
/// video editor muxes later.
/// </para>
/// </remarks>
public interface IAudioSink
{
    /// <summary>Emits one sound at one simulated time.</summary>
    /// <param name="clip">The loaded clip to play.</param>
    /// <param name="at">The simulated time of the emission, in seconds, as the tick reports it.</param>
    /// <param name="gain">The linear gain applied to this emission.</param>
    void Play(IAudioClip clip, double at, float gain = 1f);
}
