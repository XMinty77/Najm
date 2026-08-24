namespace Najm.Core.Text;

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
        "real ITypesetter — Najm.Text supplies the only one, Najm.Text.Typesetter — or pass it to " +
        "the SceneEnvironment constructor, or to OfflineOptions.Typesetter for an offline render. " +
        "Core's NullTypesetter refuses every typesetting call instead of silently producing no text.";

    private NullTypesetter()
    {
    }

    /// <summary>Gets the shared instance; the type is stateless, so one is enough.</summary>
    public static NullTypesetter Instance { get; } = new();

    /// <summary>Throws, because there is no typesetter to register a family with.</summary>
    /// <param name="family">Ignored; the call fails before it is read.</param>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public void RegisterFamily(FontFamily family) => throw Missing();

    /// <summary>Throws, because there is no typesetter to configure.</summary>
    /// <param name="textFamily">Ignored; the call fails before it is read.</param>
    /// <param name="mathFamily">Ignored; the call fails before it is read.</param>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public void SetDefaultFamilies(string textFamily, string mathFamily) => throw Missing();

    /// <summary>Throws, because there is no typesetter to measure with.</summary>
    /// <param name="face">Ignored; the call fails before it is read.</param>
    /// <param name="size">Ignored; the call fails before it is read.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public FontMetrics Metrics(FontFace face, float size) => throw Missing();

    /// <summary>Throws, because there is no typesetter to lay text out with.</summary>
    /// <param name="request">Ignored; the call fails before it is read.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public ITextLayout Typeset(in TypesetRequest request) => throw Missing();

    private static InvalidOperationException Missing() => new(Explanation);
}
