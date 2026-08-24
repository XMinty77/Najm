using HarfBuzzSharp;
using Najm.Core.Text;

namespace Najm.Text.Layout;

/// <summary>Identifies one shaped run: everything HarfBuzz reads, and nothing that only scales.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT II.5, and <strong>the single highest-leverage cache decision in the stack</strong>:
/// there is no size here. The shaper runs at <c>scale = (upem, upem)</c>, so its glyph ids,
/// advances, and offsets are font-unit facts that line layout scales by <c>size / upem</c>. One
/// entry therefore serves a label at 12 and the same label at 96, and a size tween — the documented
/// anti-idiom — still costs no reshaping at all, only relayout.
/// </para>
/// <para>
/// <see cref="Face"/> compares by handle identity, <see cref="Features"/> by structure. Direction
/// and script are constant in this Latin-oriented slice (II.4) and are keyed anyway, because the day
/// itemization produces a second value for either is the day a cache that ignored them starts
/// returning the wrong glyphs.
/// </para>
/// </remarks>
internal readonly record struct ShapedRunKey(
    FontFace Face,
    string Text,
    Direction Direction,
    Script Script,
    string Language,
    FontFeatures Features);

/// <summary>Identifies one finished layout: the content hash of NAJM-TEXT I.13.</summary>
/// <remarks>
/// <para>
/// The canonical content plus the resolved base style plus the constraints — and
/// <strong>no anchor</strong>. Anchoring is a node-side offset over the finished geometry (I.9), so
/// keying it here would split this cache twelve ways for byte-identical arrays. Forty tick labels
/// anchored <c>BaselineCenter</c> and forty anchored <c>TopLeft</c> share one entry per distinct
/// string, which is the whole point.
/// </para>
/// <para>
/// <see cref="Size"/> <em>is</em> here, because it is a typesetting input: it decides where every
/// glyph after the first one sits.
/// </para>
/// </remarks>
internal readonly record struct LayoutKey(
    string Text,
    ResolvedStyle Style,
    TextAlign Align,
    float LineSpacing);
