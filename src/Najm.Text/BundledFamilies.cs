using Najm.Core.Text;

namespace Najm.Text;

/// <summary>Builds the <see cref="FontFamily"/> values over the pinned embedded font bytes.</summary>
/// <remarks>
/// NAJM-TEXT I.2: Latin Modern Roman and Latin Modern Math are bundled defaults, so text works with
/// no configuration at all. The bytes are embedded resources with pinned lengths and SHA-256 hashes
/// (<see cref="BundledFonts"/>), which is what makes shaping deterministic: the same characters
/// shape to the same glyph ids and the same advances on every machine that runs the same build.
/// </remarks>
internal static class BundledFamilies
{
    /// <summary>Builds the Latin Modern Roman family: regular, bold, italic, and bold italic.</summary>
    /// <remarks>
    /// Latin Modern has no oblique cut and no weights other than 400 and 700, so those are the only
    /// pairs registered. A style asking for anything else fails loudly rather than being served a
    /// neighbouring face, because a substituted face has different advances and would move every
    /// glyph after it.
    /// </remarks>
    internal static FontFamily CreateRoman()
    {
        var faces = new Dictionary<(FontWeight, FontSlant), FontFace>
        {
            [(FontWeight.Normal, FontSlant.Upright)] = Face(BundledFonts.RomanRegular),
            [(FontWeight.Bold, FontSlant.Upright)] = Face(BundledFonts.RomanBold),
            [(FontWeight.Normal, FontSlant.Italic)] = Face(BundledFonts.RomanItalic),
            [(FontWeight.Bold, FontSlant.Italic)] = Face(BundledFonts.RomanBoldItalic),
        };

        return new FontFamily(BundledFonts.RomanRegular.Family, faces);
    }

    /// <summary>Builds the Latin Modern Math family, whose single face is also its math face.</summary>
    internal static FontFamily CreateMath()
    {
        var mathFace = Face(BundledFonts.Math);
        var faces = new Dictionary<(FontWeight, FontSlant), FontFace>
        {
            [(FontWeight.Normal, FontSlant.Upright)] = mathFace,
        };

        return new FontFamily(BundledFonts.Math.Family, faces, mathFace: mathFace);
    }

    private static FontFace Face(BundledFontAsset asset) => new(asset.FileName, asset.Bytes);
}
