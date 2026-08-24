namespace Najm.Core.Text;

/// <summary>Everything a typesetter needs to produce one layout, and nothing else.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.3. A request is a <strong>pure value</strong> and <c>Typeset</c> is pure compute over
/// it: the same request produces the same layout, which is what lets the typesetter content-hash the
/// request and hand back a shared immutable layout instead of building a second identical one.
/// </para>
/// <para>
/// <strong>Two deliberate absences, both cache-motivated.</strong> There is <em>no anchor</em> here —
/// anchoring is a node-side offset over the finished layout (I.9), and putting it in the key would
/// split the cache twelve ways for byte-identical geometry. There is <em>no path</em> — on-path
/// placement is a second, cheap, node-owned stage, and putting it here would turn every slide-along
/// tween into a per-frame re-typeset.
/// </para>
/// <para>
/// <strong>Fields this slice declares but does not honour throw.</strong> The house rule is that
/// author mistakes fail loud (VI.3), and the specific failure this guards against is a property that
/// accepts a value and ignores it — the author sets <see cref="MaxWidth"/>, sees text that does not
/// wrap, and has no way to tell whether the value was wrong or the feature was missing.
/// <see cref="Validate"/> names the field, the reason, and the stage it is waiting on.
/// </para>
/// </remarks>
public readonly struct TypesetRequest
{
    private readonly float lineSpacing;

    /// <summary>Creates a request for one stretch of plain text in one style.</summary>
    /// <param name="text">The content. See <see cref="Text"/>.</param>
    /// <param name="baseStyle">The style the content resolves against; it must resolve a family and a size.</param>
    public TypesetRequest(string text, Style baseStyle)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        BaseStyle = baseStyle;
        lineSpacing = 1f;
    }

    /// <summary>Gets the content to lay out.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Plain text, in one style.</strong> I.3 types this field as <c>RichContent</c> and
    /// calls plain text "a degenerate RichContent"; the rich-content span model, its markup grammar,
    /// and mathematics are a later stage, so this slice carries the degenerate case in the type it
    /// actually is. When <c>RichContent</c> lands, a plain string becomes one of its constructors and
    /// this property widens to it.
    /// </para>
    /// <para>
    /// <c>\n</c>, <c>\r\n</c>, and <c>\r</c> are hard line breaks. Nothing else breaks: automatic
    /// line breaking is UAX #14 work deferred with wrapping (II.7).
    /// </para>
    /// </remarks>
    public string Text { get; }

    /// <summary>Gets the style the content resolves against.</summary>
    /// <remarks>It must resolve both a family and a size, or the typesetter fails loudly.</remarks>
    public Style BaseStyle { get; init; }

    /// <summary>Gets the wrapping width in local units, or null — the default — for no wrapping.</summary>
    /// <remarks>
    /// <strong>This slice honours null only.</strong> Automatic line breaking is the UAX #14 stage
    /// (II.7) and is not implemented; a non-null value throws from <see cref="Validate"/> rather than
    /// laying out unwrapped text and letting the author believe the width was applied.
    /// </remarks>
    public float? MaxWidth { get; init; }

    /// <summary>Gets how lines sit horizontally in the alignment box. The default is <see cref="TextAlign.Left"/>.</summary>
    /// <remarks>
    /// With no <see cref="MaxWidth"/>, the alignment box is the natural block width — the widest
    /// line — so centring a two-line label centres the shorter line against the longer one.
    /// </remarks>
    public TextAlign Align { get; init; }

    /// <summary>Gets the leading multiplier over the face's natural line height. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite and positive.</exception>
    public float LineSpacing
    {
        get => lineSpacing;
        init
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Line spacing must be finite and positive.");
            }

            lineSpacing = value;
        }
    }

    /// <summary>Gets the extra gap added at a blank-line paragraph break. The default is 0.</summary>
    /// <remarks>
    /// <strong>This slice honours 0 only.</strong> A paragraph break is a blank line that real
    /// paragraph splitting (II.4) <em>consumes</em>, whereas hard-newline layout keeps it as an empty
    /// line of its own. Honouring the gap now would therefore bake in a spacing that changes the day
    /// paragraph splitting lands — so a non-zero value throws from <see cref="Validate"/>.
    /// </remarks>
    public float ParagraphSpacing { get; init; }

    /// <summary>Gets the request-default BCP-47 language tag, or null for the engine default.</summary>
    /// <remarks>
    /// <see cref="Style.Lang"/> overrides it. Language reaches the shaper and is part of the
    /// shaped-run cache key.
    /// </remarks>
    public string? Language { get; init; }

    /// <summary>Gets whether this layout is a node-exclusive mutable readout. The default is false.</summary>
    /// <remarks>
    /// <strong>This slice honours false only.</strong> Dynamic readouts are a separate mutable-layout
    /// path with its own digit caches and its own zero-allocation acceptance test (I.12, II.9); true
    /// throws from <see cref="Validate"/> rather than silently producing an ordinary shared layout
    /// that <c>SetValue</c> would then be unable to mutate.
    /// </remarks>
    public bool Dynamic { get; init; }

    /// <summary>
    /// Throws if this request asks for something the baseline text slice does not implement.
    /// </summary>
    /// <remarks>
    /// A typesetter calls this first, before it touches a cache, so a refused request never leaves a
    /// half-built entry behind.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// <see cref="MaxWidth"/> is set, <see cref="ParagraphSpacing"/> is non-zero, or
    /// <see cref="Dynamic"/> is true.
    /// </exception>
    public void Validate()
    {
        if (MaxWidth is { } width)
        {
            throw new NotSupportedException(
                $"TypesetRequest.MaxWidth was set to {width}, but this typesetter breaks lines only at " +
                "hard newlines: automatic line breaking (UAX #14) is not implemented yet. Leave " +
                "MaxWidth null and insert '\\n' where the line should break. The field is refused " +
                "rather than ignored so that unwrapped text is never mistaken for a wrapping bug.");
        }
        if (ParagraphSpacing != 0f)
        {
            throw new NotSupportedException(
                $"TypesetRequest.ParagraphSpacing was set to {ParagraphSpacing}, but this typesetter " +
                "has no paragraph stage: it breaks at hard newlines and keeps a blank line as an " +
                "empty line, where real paragraph splitting consumes it. Honouring the gap now would " +
                "produce spacing that changes when paragraph splitting lands. Use LineSpacing, or an " +
                "extra blank line, until then.");
        }
        if (Dynamic)
        {
            throw new NotSupportedException(
                "TypesetRequest.Dynamic was set, but the dynamic readout path — a node-exclusive " +
                "mutable layout with fixed capacity — is not implemented yet. Typeset the text " +
                "normally; a shared immutable layout cannot be mutated in place, so returning one " +
                "here would fail later and further away.");
        }
    }
}
