namespace Najm.Core.Text;

/// <summary>A named set of faces, keyed by the weight and slant a style resolves to.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.2. Latin Modern Roman and Latin Modern Math are bundled defaults, so text works with
/// no configuration and with pinned bytes — which is what makes shaping deterministic (I.13).
/// <c>RegisterFamily</c> plus <c>SetDefaultFamilies</c> is the whole swap story.
/// </para>
/// <para>
/// <strong>Matching is deterministic and exact in this slice.</strong> A style asking for a weight
/// or slant this family has no face for fails loudly at typeset rather than falling back to the
/// nearest one: a substituted face has different advances, so a silent substitution moves every
/// glyph after it and shows up as a layout that is subtly wrong on one machine and right on
/// another.
/// </para>
/// </remarks>
public sealed class FontFamily
{
    private readonly Dictionary<(FontWeight Weight, FontSlant Slant), FontFace> faces;
    private readonly string[] fallbacks;

    /// <summary>Creates a family from its faces.</summary>
    /// <param name="name">The registered name a <see cref="Style.Family"/> matches, compared ordinally and case-insensitively.</param>
    /// <param name="faces">The faces, keyed by the weight and slant each one realizes. Copied.</param>
    /// <param name="fallbacks">
    /// Family names to probe for characters this family does not cover, in order. Copied. Font
    /// fallback itself is deferred with itemization (II.4); the list is carried so a family
    /// registered today does not have to be re-registered when it lands.
    /// </param>
    /// <param name="mathFace">The face used for mathematics, or null. Mathematics is deferred (II.6).</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is whitespace, or <paramref name="faces"/> is empty.</exception>
    public FontFamily(
        string name,
        IReadOnlyDictionary<(FontWeight Weight, FontSlant Slant), FontFace> faces,
        IReadOnlyList<string>? fallbacks = null,
        FontFace? mathFace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(faces);
        if (faces.Count == 0)
        {
            throw new ArgumentException($"Font family '{name}' must contain at least one face.", nameof(faces));
        }

        this.faces = new Dictionary<(FontWeight, FontSlant), FontFace>(faces.Count);
        foreach (var (key, face) in faces)
        {
            if (face is null)
            {
                throw new ArgumentException(
                    $"Font family '{name}' has a null face at ({key.Weight}, {key.Slant}).",
                    nameof(faces));
            }

            this.faces[key] = face;
        }

        this.fallbacks = fallbacks is null ? [] : [.. fallbacks];
        Name = name;
        MathFace = mathFace;
    }

    /// <summary>Gets the registered family name.</summary>
    public string Name { get; }

    /// <summary>Gets the number of faces this family carries.</summary>
    public int FaceCount => faces.Count;

    /// <summary>Gets the family names probed, in order, for characters this family does not cover.</summary>
    public ReadOnlySpan<string> Fallbacks => fallbacks;

    /// <summary>Gets the face used for mathematics, or null when this family has none.</summary>
    public FontFace? MathFace { get; }

    /// <summary>Finds the face realizing one weight and slant.</summary>
    /// <param name="weight">The requested weight.</param>
    /// <param name="slant">The requested slant.</param>
    /// <param name="face">The matching face, or null.</param>
    /// <returns>Whether this family has a face at that pair.</returns>
    public bool TryGetFace(FontWeight weight, FontSlant slant, out FontFace? face) =>
        faces.TryGetValue((weight, slant), out face);

    /// <summary>Enumerates every face in this family, in an unspecified but stable order.</summary>
    /// <returns>The faces, keyed by the weight and slant each realizes.</returns>
    public IEnumerable<KeyValuePair<(FontWeight Weight, FontSlant Slant), FontFace>> EnumerateFaces() => faces;

    /// <inheritdoc />
    public override string ToString() => Name;
}
