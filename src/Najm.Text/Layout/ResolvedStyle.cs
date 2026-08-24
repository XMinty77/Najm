using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Text.Layout;

/// <summary>One <see cref="Style"/> after the cascade, with every optional property answered.</summary>
/// <remarks>
/// NAJM-TEXT II.3: resolution happens once, at typeset. Everything downstream — the shaped-run key,
/// the layout key, the paint table, the line metrics — reads this, never a <see cref="Style"/>, so
/// there is exactly one place where "unset" becomes a value and exactly one place a fail-loud can
/// fire for an unresolvable one.
/// </remarks>
/// <param name="Face">The matched face.</param>
/// <param name="Size">The resolved em size in local units, finite and positive.</param>
/// <param name="Color">The resolved fill color, which lands in the layout's paint table.</param>
/// <param name="LetterSpacing">The resolved tracking in local units, applied post-shape.</param>
/// <param name="Features">The resolved feature set, never null.</param>
/// <param name="Language">The resolved BCP-47 tag, never null.</param>
internal readonly record struct ResolvedStyle(
    FontFace Face,
    float Size,
    Color Color,
    float LetterSpacing,
    FontFeatures Features,
    string Language);
