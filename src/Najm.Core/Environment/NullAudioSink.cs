namespace Najm.Core;

/// <summary>The silent sink a <see cref="SceneEnvironment"/> holds until a real one is injected.</summary>
/// <remarks>
/// It accepts every emission and does nothing with it. Silence is deliberately not an error, which
/// is the opposite of <see cref="NullTypesetter"/>'s policy and for the opposite reason: a scene
/// with no audio device is an ordinary, correct configuration — every offline render is one — while
/// a scene that asks for text and gets none is a frame with something missing from it.
/// </remarks>
public sealed class NullAudioSink : IAudioSink
{
    private NullAudioSink()
    {
    }

    /// <summary>Gets the shared instance; the type is stateless, so one is enough.</summary>
    public static NullAudioSink Instance { get; } = new();

    /// <summary>Accepts the emission and discards it.</summary>
    /// <param name="clip">Ignored.</param>
    /// <param name="at">Ignored.</param>
    /// <param name="gain">Ignored.</param>
    public void Play(IAudioClip clip, double at, float gain = 1f)
    {
    }
}
