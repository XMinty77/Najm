using System.Runtime.CompilerServices;
using Najm.Core.Text;
using SkiaSharp;

namespace Najm.Skia;

/// <summary>The backend's side tables for text: typefaces, blobs, and glyph outlines.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.2 and I.4 forbid a portable handle or a layout from carrying a backend object, and
/// this is the other half of that rule: everything native that a <see cref="FontFace"/> or an
/// <see cref="ITextLayout"/> needs in order to be drawn by Skia lives here instead, keyed by the
/// portable thing it belongs to. <c>Najm.Text</c> keeps its own, independent side table for
/// HarfBuzz, keyed by the same handles; neither module can see the other's, and the two assemblies
/// never reference each other.
/// </para>
/// <para>
/// <strong>Keyed by identity, and no wider.</strong> The tables are
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>s, so an entry lives exactly as long as the
/// portable handle it hangs off and dies with it — the native handle is released by SkiaSharp's own
/// finalizer at that point. That is what makes these caches survive a warm restart (a reloaded
/// scene re-attaches to the same hot layouts and pays nothing) without becoming a leak for a font
/// that really is gone.
/// </para>
/// <para>
/// <strong>Why realization is per layout and not per draw.</strong> A layout is immutable, so a blob
/// built from it can never go stale. Building it once and reading it back on every subsequent frame
/// is what makes static text cost <em>zero</em> managed allocation in steady state (§3.6) — the
/// per-frame path is a table lookup and one native draw call per run.
/// </para>
/// </remarks>
internal static class SkiaTextResources
{
    private static readonly ConditionalWeakTable<FontFace, SKTypeface> Typefaces = [];
    private static readonly ConditionalWeakTable<ITextLayout, LayoutRealization> Realizations = [];

    /// <summary>Gets the native typeface for one portable face, creating it on first use.</summary>
    /// <exception cref="InvalidOperationException">Skia could not read the face's bytes.</exception>
    internal static SKTypeface Typeface(FontFace face) =>
        Typefaces.TryGetValue(face, out var cached) ? cached : Decode(face);

    /// <summary>Gets one layout's native realization, creating it on first draw.</summary>
    /// <remarks>
    /// <strong>The miss path lives in its own method, and that is load-bearing.</strong> A lambda
    /// that captures a local is compiled into a display class whose instance is allocated where the
    /// captured variable's <em>scope</em> begins — which, for a local declared in this method, is
    /// before the cache-hit return, not after it. Written as one method with the factory inline,
    /// this allocated 24 bytes on <em>every</em> lookup including the hits, and the steady-state
    /// zero-allocation guarantee for static text (§3.6) quietly became 24 bytes per label per frame.
    /// Splitting the miss out keeps the hit path a lookup and a return, with nothing to capture.
    /// </remarks>
    internal static LayoutRealization Realize(ITextLayout layout) =>
        Realizations.TryGetValue(layout, out var cached) ? cached : Build(layout);

    private static SKTypeface Decode(FontFace face)
    {
        using var data = SKData.CreateCopy(face.Bytes.Span);
        var typeface = SKTypeface.FromData(data, face.FaceIndex)
            ?? throw new InvalidOperationException(
                $"Skia could not read font face '{face}'. The same bytes shaped successfully, so " +
                "this is a Skia-side decode failure rather than a corrupt file.");

        // A racing caller could have won the slot; GetValue returns whichever instance is in it, so
        // the one this call decoded may be dropped rather than published. That is correct either
        // way, because a typeface over the same immutable bytes is interchangeable.
        return Typefaces.GetValue(face, _ => typeface);
    }

    private static LayoutRealization Build(ITextLayout layout) =>
        Realizations.GetValue(layout, static key => new LayoutRealization(key));

    /// <summary>One layout's fonts, blobs, and outline paths — one of each per run.</summary>
    /// <remarks>
    /// Fonts are built eagerly because they are cheap and every route needs them. Blobs and outlines
    /// are built lazily and independently: a raster render never pays for outlines it will not emit,
    /// and a vector export never builds a blob that §7.6 forbids it from writing.
    /// </remarks>
    internal sealed class LayoutRealization
    {
        private readonly SKFont[] fonts;
        private readonly SKTextBlob?[] blobs;
        private readonly SKPath?[] outlines;

        internal LayoutRealization(ITextLayout layout)
        {
            var runs = layout.Runs;
            fonts = new SKFont[runs.Length];
            blobs = new SKTextBlob?[runs.Length];
            outlines = new SKPath?[runs.Length];
            for (var index = 0; index < runs.Length; index++)
            {
                var run = runs[index];
                fonts[index] = new SKFont(Typeface(run.Font), run.Size)
                {
                    // NAJM-TEXT II.5: no hinting anywhere in the stack. Hinting would snap outlines
                    // to the pixel grid at one size and not another, which is exactly what makes
                    // font-unit shaping's "one entry serves every size" claim false.
                    Hinting = SKFontHinting.None,
                    Edging = SKFontEdging.Antialias,
                    Subpixel = true,
                };
            }
        }

        /// <summary>Gets the positioned blob for one run, building it on first use.</summary>
        internal SKTextBlob Blob(ITextLayout layout, int runIndex)
        {
            if (blobs[runIndex] is { } cached)
            {
                return cached;
            }

            var run = layout.Runs[runIndex];
            var glyphs = layout.Glyphs.Slice(run.Start, run.Count);
            var positions = layout.Positions.Slice(run.Start, run.Count);
            var points = new SKPoint[run.Count];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = new SKPoint(positions[index].X, positions[index].Y);
            }

            using var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun(glyphs, fonts[runIndex], points);
            var blob = builder.Build()
                ?? throw new InvalidOperationException(
                    $"Skia produced no text blob for a run of {run.Count} glyph(s) in '{run.Font}'.");
            blobs[runIndex] = blob;
            return blob;
        }

        /// <summary>Gets the filled outline of one run, building it on first use.</summary>
        /// <remarks>
        /// ARCHITECTURE §7.6: glyphs export as outlines by default, so publication output never
        /// depends on a viewer having the font installed. A face whose glyphs have no outlines at
        /// all — a bitmap or color face — produces an empty path here, which is the point at which
        /// the unit-rasterize failure mode would take over; the bundled Latin Modern faces are
        /// ordinary CFF outlines and always produce one.
        /// </remarks>
        internal SKPath Outline(ITextLayout layout, int runIndex)
        {
            if (outlines[runIndex] is { } cached)
            {
                return cached;
            }

            var run = layout.Runs[runIndex];
            var glyphs = layout.Glyphs.Slice(run.Start, run.Count);
            var positions = layout.Positions.Slice(run.Start, run.Count);
            var font = fonts[runIndex];
            var combined = new SKPath { FillType = SKPathFillType.Winding };
            for (var index = 0; index < glyphs.Length; index++)
            {
                using var glyphPath = font.GetGlyphPath(glyphs[index]);
                if (glyphPath is null || glyphPath.IsEmpty)
                {
                    // A blank glyph — a space — has no contours. Nothing to add, nothing wrong.
                    continue;
                }

                combined.AddPath(glyphPath, positions[index].X, positions[index].Y, SKPathAddMode.Append);
            }

            outlines[runIndex] = combined;
            return combined;
        }
    }
}
