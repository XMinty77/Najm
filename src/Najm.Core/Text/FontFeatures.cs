namespace Najm.Core.Text;

/// <summary>The OpenType features a style asks the shaper to apply.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.5. A feature set is a shaping input and therefore part of the shaped-run cache key
/// (II.5): two runs of the same text in the same face with different features are different
/// glyphs, not the same glyphs drawn differently.
/// </para>
/// <para>
/// <strong>Ligatures default on</strong>, which is why this is a class with an initializer rather
/// than a struct: <c>default(T)</c> for a struct would have meant "ligatures off", so the common
/// case would have been the one an author had to remember to ask for. A null
/// <see cref="Style.Features"/> means the same thing as a default instance and is the cheap path.
/// </para>
/// <para>
/// A font that does not implement a requested feature ignores it, deterministically. Asking for
/// small caps from a face that has none is a no-op, not an error.
/// </para>
/// </remarks>
public sealed class FontFeatures : IEquatable<FontFeatures>
{
    private readonly string[] rawTags;

    /// <summary>Creates a feature set with ligatures on and nothing else requested.</summary>
    public FontFeatures()
    {
        rawTags = [];
    }

    /// <summary>Gets the shared instance every unset <see cref="Style.Features"/> resolves to.</summary>
    public static FontFeatures Default { get; } = new();

    /// <summary>
    /// Gets whether standard and contextual ligatures apply (<c>liga</c>, <c>clig</c>). The default
    /// is true.
    /// </summary>
    /// <remarks>
    /// Turning them off is the documented escape from the one place tracking and ligatures disagree:
    /// <see cref="Style.LetterSpacing"/> is added at cluster boundaries and never inside a ligature
    /// (II.5), so heavily tracked text with <c>fi</c> still set as one glyph reads oddly. That is
    /// the author's trade-off to make, and this is the switch.
    /// </remarks>
    public bool Ligatures { get; init; } = true;

    /// <summary>Gets whether lowercase letters are shaped as small capitals (<c>smcp</c>).</summary>
    public bool SmallCaps { get; init; }

    /// <summary>Gets whether digits are shaped to a uniform advance (<c>tnum</c>).</summary>
    /// <remarks>
    /// Uniform digit advances are what keep a changing number from shuffling sideways as it counts.
    /// </remarks>
    public bool TabularNumbers { get; init; }

    /// <summary>Gets whether digits are shaped as old-style figures (<c>onum</c>).</summary>
    public bool OldstyleNums { get; init; }

    /// <summary>
    /// Gets additional four-character OpenType feature tags, applied after the named ones.
    /// </summary>
    /// <remarks>The list is copied on assignment, so a feature set stays immutable and hashable.</remarks>
    public IReadOnlyList<string> RawTags
    {
        get => rawTags;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            var copy = new string[value.Count];
            for (var index = 0; index < value.Count; index++)
            {
                var tag = value[index];
                if (tag is null || tag.Length != 4)
                {
                    throw new ArgumentException(
                        $"An OpenType feature tag must be exactly four characters; got '{tag}'.",
                        nameof(value));
                }

                copy[index] = tag;
            }

            rawTags = copy;
        }
    }

    /// <inheritdoc />
    public bool Equals(FontFeatures? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        if (other is null
            || Ligatures != other.Ligatures
            || SmallCaps != other.SmallCaps
            || TabularNumbers != other.TabularNumbers
            || OldstyleNums != other.OldstyleNums
            || rawTags.Length != other.rawTags.Length)
        {
            return false;
        }

        for (var index = 0; index < rawTags.Length; index++)
        {
            if (!string.Equals(rawTags[index], other.rawTags[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FontFeatures);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Ligatures);
        hash.Add(SmallCaps);
        hash.Add(TabularNumbers);
        hash.Add(OldstyleNums);
        foreach (var tag in rawTags)
        {
            hash.Add(tag, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
