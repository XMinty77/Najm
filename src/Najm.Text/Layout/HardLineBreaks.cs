namespace Najm.Text.Layout;

/// <summary>Splits a string at hard line breaks, and at nothing else.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT II.7: the initial slice supports hard breaks only; the greedy UAX #14 breaker is the
/// rich-text stage. What counts as a hard break here is UAX #14's mandatory-break set restricted to
/// the three sequences that actually appear in authored strings — <c>\n</c>, <c>\r\n</c>, and a lone
/// <c>\r</c>. Recognizing all three now costs one comparison and means a string pasted from a
/// Windows editor lays out the same as one typed here, instead of shaping a stray carriage return
/// into a <c>.notdef</c> box.
/// </para>
/// <para>
/// <strong>n breaks produce n+1 lines, with no special case anywhere.</strong> <c>"a\nb"</c> is two
/// lines; <c>"a\n"</c> is two, the second empty; <c>"a\n\nb"</c> is three, the middle one empty; and
/// <c>""</c> is one empty line. A rule with an exception for the trailing break would make the last
/// line of a paragraph depend on whether the author happened to end the string with one, which is
/// exactly the kind of thing nobody remembers. The blank-line case is the one paragraph splitting
/// will later change — it <em>consumes</em> the blank line rather than laying it out — which is why
/// <see cref="Najm.Core.Text.TypesetRequest.ParagraphSpacing"/> refuses to be set until then.
/// </para>
/// </remarks>
internal static class HardLineBreaks
{
    /// <summary>One line's slice of the source string.</summary>
    /// <param name="Start">The index of the line's first character.</param>
    /// <param name="Length">The number of characters, excluding the break that ended the line.</param>
    internal readonly record struct Line(int Start, int Length);

    /// <summary>Splits one string into its hard-break lines.</summary>
    /// <param name="text">The source. Empty text is one empty line.</param>
    /// <returns>The lines, in order, always at least one.</returns>
    internal static Line[] Split(string text)
    {
        var count = CountLines(text);
        var lines = new Line[count];
        var index = 0;
        var start = 0;
        var position = 0;
        while (position < text.Length)
        {
            var character = text[position];
            if (character is not ('\n' or '\r'))
            {
                position++;
                continue;
            }

            lines[index++] = new Line(start, position - start);
            position += character == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
            start = position;
        }

        lines[index] = new Line(start, text.Length - start);
        return lines;
    }

    private static int CountLines(string text)
    {
        var count = 1;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            if (character is not ('\n' or '\r'))
            {
                continue;
            }

            count++;
            if (character == '\r' && position + 1 < text.Length && text[position + 1] == '\n')
            {
                position++;
            }
        }

        return count;
    }
}
