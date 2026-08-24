using Najm.Utils;

namespace Najm.Core.Text;

/// <summary>The typographic properties one stretch of text is resolved against.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.5. Every property is optional; an unset property inherits, and in this slice — one
/// style per layout — "inherits" means "takes the engine default". Resolution happens <strong>once,
/// at typeset</strong>, into a resolved-style table; runs carry indices into it, never styles.
/// </para>
/// <para>
/// <strong>Two properties are load-bearing and must resolve.</strong> A base style that resolves no
/// family or no size fails loudly at typeset (VI.3), because there is no defensible default width
/// for text nobody sized.
/// </para>
/// <para>
/// <strong>What this slice does not carry.</strong> I.5's closed property set also names
/// <c>SizeScale</c>, <c>BaselineShiftFactor</c>, <c>Underline</c>, <c>Strikethrough</c>, and
/// <c>Tag</c>. All five are span machinery: a multiplying size factor and a baseline shift only mean
/// something composing down a cascade, decorations become <c>RuleRun</c>s the run vocabulary does
/// not yet carry, and a fragment tag addresses a fragment table that does not yet exist. They are
/// absent rather than present-and-ignored, which is the same rule the rest of this slice follows —
/// see <see cref="TypesetRequest.MaxWidth"/> for the case where a field has to exist and therefore
/// throws instead.
/// </para>
/// </remarks>
public struct Style
{
    /// <summary>Gets or sets the registered family name, or null to take the default text family.</summary>
    public string? Family { get; set; }

    /// <summary>Gets or sets the weight, or null for <see cref="FontWeight.Normal"/>.</summary>
    public FontWeight? Weight { get; set; }

    /// <summary>Gets or sets the slant, or null for <see cref="FontSlant.Upright"/>.</summary>
    public FontSlant? Slant { get; set; }

    /// <summary>Gets or sets the em size in local units, or null to inherit.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Size is a typesetting input and a cache key</strong> (I.10, ARCHITECTURE §12.3).
    /// Changing it re-typesets and produces a new cache entry.
    /// </para>
    /// <para>
    /// <strong>The documented idiom for growing or shrinking text is
    /// <c>Transform.Scale</c></strong>, not a size tween: scaling is vector-crisp, visually
    /// identical, and costs no relayout at all, whereas a size tween re-typesets every frame it
    /// runs. A size tween still works — it is not forbidden — and the debug overlay's content
    /// transition counter is where the difference becomes visible.
    /// </para>
    /// </remarks>
    public float? Size { get; set; }

    /// <summary>Gets or sets the fill color, or null for opaque black.</summary>
    /// <remarks>
    /// A node's uniform color is a <em>draw-time</em> override (I.4) rather than this property, so
    /// recoloring a label — including tweening its color — never re-typesets it. This is the color
    /// baked into the layout's paint table, which the override replaces.
    /// </remarks>
    public Color? Color { get; set; }

    /// <summary>Gets or sets extra tracking in local units, or null for none.</summary>
    /// <remarks>
    /// Applied <strong>post-shape</strong>, at cluster boundaries only, so it never opens a gap
    /// inside a ligature (II.5). Negative values tighten.
    /// </remarks>
    public float? LetterSpacing { get; set; }

    /// <summary>Gets or sets the OpenType features, or null for <see cref="FontFeatures.Default"/>.</summary>
    public FontFeatures? Features { get; set; }

    /// <summary>Gets or sets the BCP-47 language tag this text shapes as, or null to inherit.</summary>
    /// <remarks>
    /// Language reaches HarfBuzz and can change which glyphs a face produces for the same
    /// characters, so it is part of the shaped-run cache key.
    /// </remarks>
    public string? Lang { get; set; }
}
