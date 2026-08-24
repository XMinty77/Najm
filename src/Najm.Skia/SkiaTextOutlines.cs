using Najm.Core;
using Najm.Core.Text;
using SkiaSharp;

namespace Najm.Skia;

/// <summary>Turns a finished text layout into portable filled outlines.</summary>
/// <remarks>
/// <para>
/// ARCHITECTURE §7.6: <strong>glyphs export as outlines by default</strong>, because publication
/// output must not depend on a viewer having the font installed. The export contexts consume that
/// rule internally — <see cref="SkiaDrawContext2D.DrawText"/> takes the outline route whenever
/// <see cref="RenderCaps.VectorTarget"/> is set — and this is the same machinery reached
/// deliberately, which is also §7.6's <c>ITextLayout.BakePath</c> seam: text-shaped clips, masks, and
/// morph sources all want the contours rather than the drawing.
/// </para>
/// <para>
/// <strong>The overlap caveat, stated.</strong> The result carries
/// <see cref="FillRule.NonZero"/>, so overlapping glyph contours — a tight script, a heavily negative
/// tracking — fill solid. An even-odd consumer would punch holes where two letters touch. That is the
/// documented behaviour, not an accident of the implementation.
/// </para>
/// <para>
/// The path is in the layout's reading frame, exactly like <see cref="ITextLayout.Positions"/>, so a
/// caller that has a text node's reading-to-local transform applies the same one here.
/// </para>
/// </remarks>
public static class SkiaTextOutlines
{
    /// <summary>Bakes every glyph contour of a layout into one portable path.</summary>
    /// <param name="layout">The layout to outline.</param>
    /// <returns>
    /// A fresh <see cref="PathBuilder"/> carrying every contour, with
    /// <see cref="FillRule.NonZero"/>. A layout with no glyphs — or one whose face has no outlines
    /// at all — produces an empty path rather than a null one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    public static PathBuilder BakePath(ITextLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var builder = new PathBuilder(FillRule.NonZero);
        var realization = SkiaTextResources.Realize(layout);
        var runs = layout.Runs;
        for (var index = 0; index < runs.Length; index++)
        {
            Append(builder, realization.Outline(layout, index));
        }

        return builder;
    }

    /// <summary>Copies one native path's verbs into a portable builder.</summary>
    /// <remarks>
    /// Conics are the one verb <see cref="PathBuilder"/> does not carry, and glyph outlines never
    /// contain them: TrueType contours are quadratics and CFF contours are cubics. A conic
    /// nonetheless fails loudly rather than being dropped or approximated, because a silently
    /// missing contour is a letter with a hole in it.
    /// </remarks>
    private static void Append(PathBuilder builder, SKPath path)
    {
        if (path.IsEmpty)
        {
            return;
        }

        using var iterator = path.CreateRawIterator();
        Span<SKPoint> points = stackalloc SKPoint[4];
        while (true)
        {
            switch (iterator.Next(points))
            {
                case SKPathVerb.Move:
                    builder.MoveTo(points[0].X, points[0].Y);
                    break;
                case SKPathVerb.Line:
                    builder.LineTo(points[1].X, points[1].Y);
                    break;
                case SKPathVerb.Quad:
                    builder.QuadTo(points[1].X, points[1].Y, points[2].X, points[2].Y);
                    break;
                case SKPathVerb.Cubic:
                    builder.CubicTo(
                        points[1].X,
                        points[1].Y,
                        points[2].X,
                        points[2].Y,
                        points[3].X,
                        points[3].Y);
                    break;
                case SKPathVerb.Close:
                    builder.Close();
                    break;
                case SKPathVerb.Done:
                    return;
                default:
                    throw new NotSupportedException(
                        "A glyph outline contained a conic section, which the portable path model " +
                        "does not carry. TrueType outlines are quadratic and CFF outlines are cubic, " +
                        "so this indicates a face this backend has not been taught to lower rather " +
                        "than a contour that may be dropped.");
            }
        }
    }
}
