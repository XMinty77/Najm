namespace Najm.Core.Text;

/// <summary>Names the weight axis of a font family, on the usual 100–900 scale.</summary>
/// <remarks>
/// The numeric values are the OpenType/CSS usWeightClass numbers, so a family that keys its faces
/// by weight keys them by a number an author already knows. Face matching is exact in this slice:
/// a family that has no face at the requested weight fails loudly rather than substituting a
/// neighbour, because a silently substituted face changes advances and therefore layout.
/// </remarks>
public enum FontWeight
{
    /// <summary>100.</summary>
    Thin = 100,

    /// <summary>200.</summary>
    ExtraLight = 200,

    /// <summary>300.</summary>
    Light = 300,

    /// <summary>400 — the default.</summary>
    Normal = 400,

    /// <summary>500.</summary>
    Medium = 500,

    /// <summary>600.</summary>
    SemiBold = 600,

    /// <summary>700 — what <c>&lt;b&gt;</c> and <c>TextNode.Bold</c> resolve to.</summary>
    Bold = 700,

    /// <summary>800.</summary>
    ExtraBold = 800,

    /// <summary>900.</summary>
    Black = 900,
}

/// <summary>Names the slant axis of a font family.</summary>
public enum FontSlant
{
    /// <summary>Upright — the default.</summary>
    Upright,

    /// <summary>A drawn italic, with its own letterforms.</summary>
    Italic,

    /// <summary>A slanted upright, with the upright letterforms sheared.</summary>
    Oblique,
}

/// <summary>Places each line horizontally inside the layout's alignment box.</summary>
/// <remarks>
/// <c>Justify</c> is deferred with the rest of the wrapping stage (NAJM-TEXT I.3): justification is
/// a property of a wrapped paragraph, and this slice breaks only at hard newlines.
/// </remarks>
public enum TextAlign
{
    /// <summary>Every line starts at the alignment box's left edge. The default.</summary>
    Left,

    /// <summary>Every line is centred in the alignment box.</summary>
    Center,

    /// <summary>Every line ends at the alignment box's right edge.</summary>
    Right,
}

/// <summary>
/// Names the point of a text node's layout that sits at the node's local origin.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An anchor is a node-side offset, never a typeset input</strong> (NAJM-TEXT I.9). The
/// layout is the same geometry whichever anchor is chosen, so putting the anchor in
/// <see cref="TypesetRequest"/> would split the layout cache twelve ways for identical arrays.
/// </para>
/// <para>
/// <strong>Baseline anchors are baseline-true:</strong> they reference the <em>first line's
/// baseline</em>, not a box edge, which is why <see cref="BaselineLeft"/> is the default. A tick
/// label anchored that way sits on its tick without ascent arithmetic, and two labels at different
/// sizes align along the baseline they share rather than along the tops of their tallest letters.
/// The other nine reference <see cref="ITextLayout.LogicalBounds"/>.
/// </para>
/// <para>
/// <strong>Top and bottom are visual, not numeric.</strong> The anchor is resolved in the reading
/// frame and the upright rule's flip is applied after it, so <see cref="TopCenter"/> names the top
/// of the text as it reads in a screen layer and in a Y-up world layer alike.
/// </para>
/// </remarks>
public enum TextAnchor
{
    /// <summary>The first line's baseline at the alignment box's left edge. The default.</summary>
    BaselineLeft,

    /// <summary>The first line's baseline at the alignment box's horizontal centre.</summary>
    BaselineCenter,

    /// <summary>The first line's baseline at the alignment box's right edge.</summary>
    BaselineRight,

    /// <summary>The logical box's top-left corner.</summary>
    TopLeft,

    /// <summary>The centre of the logical box's top edge.</summary>
    TopCenter,

    /// <summary>The logical box's top-right corner.</summary>
    TopRight,

    /// <summary>The centre of the logical box's left edge.</summary>
    CenterLeft,

    /// <summary>The centre of the logical box.</summary>
    Center,

    /// <summary>The centre of the logical box's right edge.</summary>
    CenterRight,

    /// <summary>The logical box's bottom-left corner.</summary>
    BottomLeft,

    /// <summary>The centre of the logical box's bottom edge.</summary>
    BottomCenter,

    /// <summary>The logical box's bottom-right corner.</summary>
    BottomRight,
}
