namespace Najm.Core;

/// <summary>The fail-loud typesetter a <see cref="SceneEnvironment"/> holds until a real one is injected.</summary>
/// <remarks>
/// It throws on every call rather than returning empty layouts, because a scene that asks to
/// typeset something and silently gets nothing renders a blank frame with no explanation. Every
/// message names both the option to set and the assembly that supplies the implementation, so the
/// exception itself is the fix. A host that shows any text at all injects the real typesetter.
/// </remarks>
public sealed class NullTypesetter : ITypesetter
{
    /// <summary>
    /// The reason and the fix, shared by every member so no call site can report a weaker one.
    /// </summary>
    private const string Explanation =
        "No typesetter is installed in this scene's environment. Set HostOptions.Typesetter to a " +
        "real ITypesetter — Najm.Text supplies the only one — or pass it to the SceneEnvironment " +
        "constructor. Core's NullTypesetter refuses every typesetting call instead of silently " +
        "producing no text.";

    private NullTypesetter()
    {
    }

    /// <summary>Gets the shared instance; the type is stateless, so one is enough.</summary>
    public static NullTypesetter Instance { get; } = new();

    /// <summary>Throws, because there is no typesetter to configure.</summary>
    /// <param name="textFamily">Ignored; the call fails before it is read.</param>
    /// <param name="mathFamily">Ignored; the call fails before it is read.</param>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public void SetDefaultFamilies(string textFamily, string mathFamily) => throw Missing();

    private static InvalidOperationException Missing() => new(Explanation);
}
