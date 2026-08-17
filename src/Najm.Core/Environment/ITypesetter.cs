namespace Najm.Core;

/// <summary>Measures, shapes, and lays out every piece of text the engine draws.</summary>
/// <remarks>
/// <para>
/// <strong>Placeholder.</strong> This interface exists so <see cref="SceneEnvironment"/> can be a
/// closed set of five capabilities today, and it carries exactly one member — enough to exist, and
/// enough for <see cref="NullTypesetter"/> to throw from. The real surface —
/// <c>Metrics</c>, <c>Typeset</c> over a request/layout vocabulary, and the text-on-path
/// <c>Place</c> stage, together with the portable font and rich-content model they speak — lands
/// with <c>Najm.Text</c>, which is the sole producer of a real typesetter. Nothing may depend on
/// the shape of this type until then.
/// </para>
/// <para>
/// A typesetter is one per environment and render-thread affine, like every other engine service.
/// Nodes never shape text and draw contexts never shape text: both go through here.
/// </para>
/// </remarks>
public interface ITypesetter
{
    /// <summary>Sets the families a style cascade falls back to for text and for mathematics.</summary>
    /// <param name="textFamily">The registered family name used when a style resolves no text family.</param>
    /// <param name="mathFamily">The registered family name used when a style resolves no math family.</param>
    void SetDefaultFamilies(string textFamily, string mathFamily);
}
