using System.Numerics;
using HarfBuzzSharp;
using Najm.Core;
using Najm.Core.Text;
using Najm.Text.HarfBuzz;
using Najm.Text.Layout;
using Najm.Utils;
using CoreRect = Najm.Core.Rect;

namespace Najm.Text;

/// <summary>The engine's one real <see cref="ITypesetter"/>: shaping, line layout, and the caches.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT II.1. Construction order is the family registry — with the pinned Latin Modern defaults
/// already in it — then the lazily built per-face HarfBuzz side tables, then the caches. One
/// instance per environment, <strong>render-thread affine</strong> like every other engine service:
/// every call arrives from the frame thread or the load phase, and there is no internal locking to
/// pay for. Using one from a second thread is refused rather than raced.
/// </para>
/// <para>
/// <strong>The pipeline, for this slice.</strong> Plain text → style resolution (II.3) → one
/// Latin-oriented shaping item per line (II.4) → HarfBuzz shaping in font units (II.5) → line layout
/// (II.7) → an immutable layout. Markup, mathematics, bidi, script itemization, font fallback,
/// wrapping, fragments, and text-on-path are later stages; each is refused loudly rather than
/// approximated, so nothing here has to be unlearned when they arrive.
/// </para>
/// <para>
/// <strong>Two caches, one of which does the real work.</strong> The layout cache dedups whole
/// results by content hash, so two nodes with the same string in the same style hold the same
/// <see cref="ITextLayout"/> instance. Underneath it, the shaped-run cache is keyed <em>without</em>
/// size, because shaping happens in font units; that is what makes one shaped entry serve every size
/// a label is drawn at (II.5).
/// </para>
/// </remarks>
public sealed class Typesetter : ITypesetter, IDisposable
{
    /// <summary>The bundled text family's registered name.</summary>
    public const string LatinModernRoman = "Latin Modern Roman";

    /// <summary>The bundled math family's registered name.</summary>
    public const string LatinModernMath = "Latin Modern Math";

    /// <summary>
    /// The language a request that names none shapes as.
    /// </summary>
    /// <remarks>
    /// A language reaches HarfBuzz and can change which glyphs a face produces, so it has to be
    /// <em>something</em> definite: leaving it to the ambient culture would make a layout depend on
    /// the machine that produced it, which is precisely what §2.2's determinism posture forbids.
    /// </remarks>
    public const string DefaultLanguage = "en";

    /// <summary>The direction this Latin-oriented slice shapes every run in (II.4).</summary>
    private const Direction ShapingDirection = Direction.LeftToRight;

    private static readonly Feature[] NoFeatures = [];
    private static readonly Language[] LanguageCacheKeys = [];

    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, FontFamily> families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FontFace, FaceEntry> faceEntries = [];
    private readonly Dictionary<string, Language> languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ShapedRunKey, ShapedRun> shapedRuns = [];
    private readonly Dictionary<LayoutKey, TextLayout> layouts = [];
    private readonly Script shapingScript = Script.Latin;
    private string defaultTextFamily = LatinModernRoman;
    private string defaultMathFamily = LatinModernMath;
    private bool disposed;

    /// <summary>Creates a typesetter with the bundled Latin Modern families already registered.</summary>
    /// <remarks>
    /// The embedded bytes are verified — length and SHA-256 — the first time a face is touched, so a
    /// corrupted or substituted resource is a loud failure at load rather than wrong glyphs at
    /// render.
    /// </remarks>
    public Typesetter()
    {
        RegisterFamily(BundledFamilies.CreateRoman());
        RegisterFamily(BundledFamilies.CreateMath());
    }

    /// <summary>Gets the number of distinct layouts this typesetter is currently holding.</summary>
    /// <remarks>
    /// Diagnostics, and the honest way to assert the cache claims: a color tween that changes no
    /// cache entry count is a color tween that re-typeset nothing.
    /// </remarks>
    public int CachedLayoutCount => layouts.Count;

    /// <summary>Gets the number of distinct shaped runs this typesetter is currently holding.</summary>
    /// <remarks>
    /// Size-independent by construction, so laying the same string out at ten sizes leaves this at
    /// one.
    /// </remarks>
    public int CachedShapedRunCount => shapedRuns.Count;

    /// <inheritdoc />
    public void RegisterFamily(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        EnsureUsable();
        families[family.Name] = family;
    }

    /// <inheritdoc />
    public void SetDefaultFamilies(string textFamily, string mathFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(mathFamily);
        EnsureUsable();

        if (!families.ContainsKey(textFamily))
        {
            throw new ArgumentException(UnknownFamily(textFamily), nameof(textFamily));
        }
        if (!families.ContainsKey(mathFamily))
        {
            throw new ArgumentException(UnknownFamily(mathFamily), nameof(mathFamily));
        }

        defaultTextFamily = textFamily;
        defaultMathFamily = mathFamily;
    }

    /// <summary>Gets the family name a style resolving no family falls back to.</summary>
    public string DefaultTextFamily => defaultTextFamily;

    /// <summary>Gets the family name mathematics resolves against. Mathematics itself is deferred.</summary>
    public string DefaultMathFamily => defaultMathFamily;

    /// <inheritdoc />
    public FontMetrics Metrics(FontFace face, float size)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (!float.IsFinite(size) || size <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A font size must be finite and positive.");
        }

        EnsureUsable();
        var entry = Entry(face);
        var scale = size / entry.UnitsPerEm;
        return new FontMetrics(entry.Ascent * scale, entry.Descent * scale, entry.LineGap * scale);
    }

    /// <inheritdoc />
    public ITextLayout Typeset(in TypesetRequest request)
    {
        EnsureUsable();

        // Refuse before touching a cache, so a refused request never leaves half an entry behind.
        request.Validate();

        var text = request.Text
            ?? throw new ArgumentException("A typeset request needs its text.", nameof(request));
        var style = Resolve(request);
        var key = new LayoutKey(text, style, request.Align, request.LineSpacing);
        if (layouts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var built = Build(text, style, request.Align, request.LineSpacing);
        layouts[key] = built;
        return built;
    }

    /// <summary>Releases every HarfBuzz face and buffer this typesetter created.</summary>
    /// <remarks>
    /// Layouts handed out before disposal remain valid: they are portable arrays and hold nothing
    /// native. Only further typesetting is refused.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        EnsureOwnerThread();
        disposed = true;
        foreach (var entry in faceEntries.Values)
        {
            entry.Dispose();
        }

        faceEntries.Clear();
        shapedRuns.Clear();
        layouts.Clear();
    }

    /// <summary>Folds a request's base style into the fully answered form II.3 produces.</summary>
    private ResolvedStyle Resolve(in TypesetRequest request)
    {
        var style = request.BaseStyle;
        var familyName = style.Family ?? defaultTextFamily;
        if (!families.TryGetValue(familyName, out var family))
        {
            throw new InvalidOperationException(UnknownFamily(familyName));
        }

        var weight = style.Weight ?? FontWeight.Normal;
        var slant = style.Slant ?? FontSlant.Upright;
        if (!family.TryGetFace(weight, slant, out var face) || face is null)
        {
            throw new InvalidOperationException(
                $"Font family '{family.Name}' has no {weight}/{slant} face. It has: " +
                $"{DescribeFaces(family)}. Face matching is exact — a near miss is refused rather " +
                "than substituted, because a substituted face has different advances and would move " +
                "every glyph after it.");
        }

        if (style.Size is not { } size)
        {
            throw new InvalidOperationException(
                "A typeset request's base style must resolve a size, and this one resolves none. " +
                "Set Style.Size (local units); there is no defensible default width for text nobody " +
                "sized. Node surfaces such as TextNode.Size supply it for you.");
        }
        if (!float.IsFinite(size) || size <= 0f)
        {
            throw new InvalidOperationException($"A resolved font size must be finite and positive; got {size}.");
        }

        var letterSpacing = style.LetterSpacing ?? 0f;
        if (!float.IsFinite(letterSpacing))
        {
            throw new InvalidOperationException($"Letter spacing must be finite; got {letterSpacing}.");
        }

        return new ResolvedStyle(
            face,
            size,
            style.Color ?? Color.Black,
            letterSpacing,
            style.Features ?? FontFeatures.Default,
            style.Lang ?? request.Language ?? DefaultLanguage);
    }

    /// <summary>Runs II.7's line layout over the shaped lines and freezes the result.</summary>
    private TextLayout Build(string text, in ResolvedStyle style, TextAlign align, float lineSpacing)
    {
        var entry = Entry(style.Face);
        var sourceLines = HardLineBreaks.Split(text);
        var scale = style.Size / entry.UnitsPerEm;
        var ascent = entry.Ascent * scale;
        var descent = entry.Descent * scale;
        var baselineAdvance = (entry.Ascent + entry.Descent + entry.LineGap) * scale * lineSpacing;

        var shaped = new ShapedRun[sourceLines.Length];
        var widths = new float[sourceLines.Length];
        var glyphTotal = 0;
        var blockWidth = 0f;
        var language = LanguageFor(style.Language);
        var features = FeaturesFor(style.Features);
        for (var index = 0; index < sourceLines.Length; index++)
        {
            var line = sourceLines[index];
            var run = ShapeCached(entry, text.AsSpan(line.Start, line.Length), language, features, style);
            shaped[index] = run;
            widths[index] = AdvanceWidth(run, scale, style.LetterSpacing);
            glyphTotal += run.Glyphs.Length;
            blockWidth = MathF.Max(blockWidth, widths[index]);
        }

        var glyphs = new ushort[glyphTotal];
        var positions = new Vector2[glyphTotal];
        var clusters = new int[glyphTotal];
        var lines = new LineMetrics[sourceLines.Length];
        var runs = new List<GlyphRun>(sourceLines.Length);
        var inkLeft = float.PositiveInfinity;
        var inkTop = float.PositiveInfinity;
        var inkRight = float.NegativeInfinity;
        var inkBottom = float.NegativeInfinity;
        var cursor = 0;

        for (var index = 0; index < sourceLines.Length; index++)
        {
            var source = sourceLines[index];
            var run = shaped[index];
            var baseline = index * baselineAdvance;
            var left = AlignmentOffset(align, blockWidth, widths[index]);
            var pen = left;
            var glyphStart = cursor;

            var runGlyphs = run.Glyphs;
            for (var glyphIndex = 0; glyphIndex < runGlyphs.Length; glyphIndex++)
            {
                var glyph = runGlyphs[glyphIndex];
                var x = pen + (glyph.XOffset * scale);

                // HarfBuzz offsets are y-up; the reading frame is y-down, so the sign flips exactly
                // here and nowhere else.
                var y = baseline - (glyph.YOffset * scale);
                glyphs[cursor] = checked((ushort)glyph.GlyphId);
                positions[cursor] = new Vector2(x, y);
                clusters[cursor] = source.Start + (int)glyph.Cluster;
                cursor++;

                var box = entry.GlyphInkBox(glyph.GlyphId);
                if (box.Width != 0 && box.Height != 0)
                {
                    var boxLeft = x + (box.Left * scale);
                    var boxTop = y + (box.Top * scale);
                    inkLeft = MathF.Min(inkLeft, boxLeft);
                    inkTop = MathF.Min(inkTop, boxTop);
                    inkRight = MathF.Max(inkRight, boxLeft + (box.Width * scale));
                    inkBottom = MathF.Max(inkBottom, boxTop + (box.Height * scale));
                }

                pen += glyph.XAdvance * scale;
                if (IsClusterEnd(runGlyphs, glyphIndex))
                {
                    pen += style.LetterSpacing;
                }
            }

            lines[index] = new LineMetrics(
                baseline,
                left,
                widths[index],
                ascent,
                descent,
                glyphStart,
                cursor - glyphStart,
                source.Start,
                source.Length);

            if (cursor > glyphStart)
            {
                runs.Add(new GlyphRun(style.Face, style.Size, paintIndex: 0, glyphStart, cursor - glyphStart, index));
            }
        }

        var logicalBounds = new CoreRect(
            0f,
            -ascent,
            blockWidth,
            ((sourceLines.Length - 1) * baselineAdvance) + ascent + descent);
        var inkBounds = inkRight > inkLeft && inkBottom > inkTop
            ? new CoreRect(inkLeft, inkTop, inkRight - inkLeft, inkBottom - inkTop)
            : default;

        return new TextLayout(
            glyphs,
            positions,
            clusters,
            [.. runs],
            lines,
            [style.Color],
            logicalBounds,
            inkBounds);
    }

    /// <summary>
    /// Returns the advance width of one shaped line at a size, including its letter spacing.
    /// </summary>
    /// <remarks>
    /// <strong>Tracking is added after every cluster, the last one included</strong> — the CSS
    /// <c>letter-spacing</c> rule. The alternative, skipping the trailing one, makes a line's width
    /// depend on where it ends and leaves centred text sitting half a track to the left of where the
    /// same rule put it in every other tool. Consequence, stated so nobody has to measure it: a
    /// tracked line's width is the untracked width plus <c>clusters × spacing</c>.
    /// </remarks>
    private static float AdvanceWidth(ShapedRun run, float scale, float letterSpacing)
    {
        var width = run.TotalXAdvance * scale;
        if (letterSpacing == 0f)
        {
            return width;
        }

        var glyphs = run.Glyphs;
        for (var index = 0; index < glyphs.Length; index++)
        {
            if (IsClusterEnd(glyphs, index))
            {
                width += letterSpacing;
            }
        }

        return width;
    }

    /// <summary>
    /// Reports whether a glyph is the last one of its cluster, which is where tracking lands.
    /// </summary>
    /// <remarks>
    /// II.5's rule: never inside a ligature. A ligature is one glyph carrying one cluster that spans
    /// several characters, so it takes exactly one track at its end and none in its middle — which
    /// falls out of this test rather than needing a ligature-specific case.
    /// </remarks>
    private static bool IsClusterEnd(ReadOnlySpan<ShapedGlyph> glyphs, int index) =>
        index + 1 >= glyphs.Length || glyphs[index + 1].Cluster != glyphs[index].Cluster;

    private static float AlignmentOffset(TextAlign align, float blockWidth, float lineWidth) => align switch
    {
        TextAlign.Left => 0f,
        TextAlign.Center => (blockWidth - lineWidth) * 0.5f,
        TextAlign.Right => blockWidth - lineWidth,
        _ => throw new ArgumentOutOfRangeException(nameof(align), align, "The text alignment is not defined."),
    };

    private ShapedRun ShapeCached(
        FaceEntry entry,
        ReadOnlySpan<char> text,
        Language language,
        Feature[] features,
        in ResolvedStyle style)
    {
        // The key's text is a string because a cache key has to outlive the call; a line of a label
        // is short and this happens once per distinct line, not once per frame.
        var key = new ShapedRunKey(
            entry.Face,
            text.ToString(),
            ShapingDirection,
            shapingScript,
            style.Language,
            style.Features);
        if (shapedRuns.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var shaped = entry.Shape(text, ShapingDirection, shapingScript, language, features);
        shapedRuns[key] = shaped;
        return shaped;
    }

    private FaceEntry Entry(FontFace face)
    {
        if (faceEntries.TryGetValue(face, out var entry))
        {
            return entry;
        }

        entry = new FaceEntry(face);
        faceEntries[face] = entry;
        return entry;
    }

    private Language LanguageFor(string tag)
    {
        if (languages.TryGetValue(tag, out var cached))
        {
            return cached;
        }

        var language = new Language(tag);
        languages[tag] = language;
        return language;
    }

    /// <summary>Lowers a feature set to the HarfBuzz array II.5 hands the shaper.</summary>
    /// <remarks>
    /// The default set lowers to an empty array rather than to an explicit "ligatures on": HarfBuzz
    /// already applies <c>liga</c>, <c>clig</c>, and kerning by default, so the common case allocates
    /// nothing and asks for exactly what the font intends.
    /// </remarks>
    private static Feature[] FeaturesFor(FontFeatures features)
    {
        var count = (features.Ligatures ? 0 : 2)
            + (features.SmallCaps ? 1 : 0)
            + (features.TabularNumbers ? 1 : 0)
            + (features.OldstyleNums ? 1 : 0)
            + features.RawTags.Count;
        if (count == 0)
        {
            return NoFeatures;
        }

        var array = new Feature[count];
        var index = 0;
        if (!features.Ligatures)
        {
            array[index++] = new Feature(new Tag('l', 'i', 'g', 'a'), 0u);
            array[index++] = new Feature(new Tag('c', 'l', 'i', 'g'), 0u);
        }
        if (features.SmallCaps)
        {
            array[index++] = new Feature(new Tag('s', 'm', 'c', 'p'), 1u);
        }
        if (features.TabularNumbers)
        {
            array[index++] = new Feature(new Tag('t', 'n', 'u', 'm'), 1u);
        }
        if (features.OldstyleNums)
        {
            array[index++] = new Feature(new Tag('o', 'n', 'u', 'm'), 1u);
        }

        foreach (var tag in features.RawTags)
        {
            array[index++] = new Feature(new Tag(tag[0], tag[1], tag[2], tag[3]), 1u);
        }

        return array;
    }

    private string UnknownFamily(string name) =>
        $"No font family named '{name}' is registered. Registered families: " +
        $"{string.Join(", ", families.Keys.Order(StringComparer.Ordinal))}. Call " +
        "ITypesetter.RegisterFamily before a style names it.";

    private static string DescribeFaces(FontFamily family) =>
        string.Join(
            ", ",
            family.EnumerateFaces()
                .Select(pair => $"{pair.Key.Weight}/{pair.Key.Slant}")
                .Order(StringComparer.Ordinal));

    private void EnsureUsable()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThreadId)
        {
            throw new InvalidOperationException(
                "A typesetter is render-thread affine: it must be used and disposed on the thread " +
                "that created it. NAJM-TEXT II.1 — no internal locking, by design.");
        }
    }
}
