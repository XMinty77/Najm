using Najm.Core.Text;
using Najm.Text;

namespace Najm.Lib.Tests;

/// <summary>Counts what a real typesetter is asked to do, and forwards every call to it.</summary>
/// <remarks>
/// A decorator rather than a fake, so the geometry a test reasons about is the geometry the engine
/// produces — the claims under test here are about <em>how often</em> the typesetter is called, and
/// a fake would make the "how often" easy and the "what came back" meaningless.
/// </remarks>
internal sealed class CountingTypesetter(ITypesetter inner) : ITypesetter, IDisposable
{
    /// <summary>Gets how many times a layout has been asked for.</summary>
    internal int TypesetCount { get; private set; }

    /// <summary>Gets how many times metrics have been asked for.</summary>
    internal int MetricsCount { get; private set; }

    /// <summary>Creates a counter over a fresh real typesetter.</summary>
    internal static CountingTypesetter Real() => new(new Typesetter());

    /// <summary>Gets the distinct layouts the wrapped typesetter is holding.</summary>
    internal int CachedLayoutCount => ((Typesetter)inner).CachedLayoutCount;

    public void RegisterFamily(FontFamily family) => inner.RegisterFamily(family);

    public void SetDefaultFamilies(string textFamily, string mathFamily) =>
        inner.SetDefaultFamilies(textFamily, mathFamily);

    public FontMetrics Metrics(FontFace face, float size)
    {
        MetricsCount++;
        return inner.Metrics(face, size);
    }

    public ITextLayout Typeset(in TypesetRequest request)
    {
        TypesetCount++;
        return inner.Typeset(request);
    }

    public void Dispose() => (inner as IDisposable)?.Dispose();
}
