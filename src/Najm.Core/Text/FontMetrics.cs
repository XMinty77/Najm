namespace Najm.Core.Text;

/// <summary>The vertical metrics of one face at one size, in local units.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.2: <strong>ascent and descent are positive magnitudes</strong>, whichever sign the
/// font file or the backend uses internally. An engine that passes a negative descent around
/// eventually adds it somewhere that wanted to subtract it, and the bug is a line of text one
/// descender too high on one platform.
/// </para>
/// <para>
/// The values scale linearly with size, because nothing in this stack hints (II.5). Metrics at 24
/// are exactly twice metrics at 12.
/// </para>
/// </remarks>
/// <param name="Ascent">How far the face rises above the baseline, positive.</param>
/// <param name="Descent">How far the face falls below the baseline, positive.</param>
/// <param name="LineGap">The face's recommended extra leading between lines, usually zero or positive.</param>
public readonly record struct FontMetrics(float Ascent, float Descent, float LineGap)
{
    /// <summary>
    /// Gets the face's natural baseline-to-baseline distance: ascent plus descent plus line gap.
    /// </summary>
    /// <remarks>
    /// <see cref="TypesetRequest.LineSpacing"/> multiplies this to get the layout's actual baseline
    /// advance (II.7).
    /// </remarks>
    public float LineHeight => Ascent + Descent + LineGap;
}
