# NAJM-TEXT — Text & Typesetting Architecture

**Status.** Current text and typesetting companion to `ARCHITECTURE.md`. It owns the portable text model, shaping and layout pipeline, mathematics, fragments, text-on-path, caches, interaction data, and the backend-lowering boundary.

**Boundary rule.** Author-observable text semantics are summarized in the engine architecture; this document defines exact data shapes and realization.

---

## 0. Pinned dependencies and the check discipline

| Dependency | Pin | Role | Notes |
|---|---|---|---|
| **HarfBuzzSharp** | exact version pinned with the SkiaSharp family in `Directory.Packages.props` | shaping (II.5) | the pinned version joins the §2.2 reproducibility environment |
| **CSharpMath** + **CSharpMath.SkiaSharp** | exact compatible version pinned in `Directory.Packages.props` | Fast math flavor (II.6) | the II.6 adapter isolates its display-tree API; the portable `VectorPictureRun` fallback preserves correctness if extraction is unavailable |
| **Unicode Character Database** | exact version pinned in the generated-data manifest | UAX #9 / #14 / #24 / #29 property tables (II.11) | build-time generated tables; the version joins §2.2 reproducibility; conformance tests run per pin (App. C) |
| **Latin Modern Roman / Latin Modern Math** | embedded resources (GUST Font License) | bundled default families (I.2) | pinned bytes ⇒ deterministic zero-config text; swappable via `RegisterFamily` + `SetDefaultFamilies` |
| **latex + dvisvgm** | external toolchain, distro-stamped | Full math flavor (Part IV) | offline / load-time only; never a runtime frame dependency; absence is a fail-loud with install hint |

**Check discipline.** Same rules as NAJM-SKIA Appendix A: every binding or toolchain fact this design leans on gets a **first-use check** with a cached verdict, a stated correct-but-degraded fallback, and a loud log. Ownership is by dependency: **HB-R##** (HarfBuzzSharp) and **CS-R##** (CSharpMath) live in this document's Appendix A; **SkiaSharp** behaviors stay in NAJM-SKIA's registry (SK-R13–R15, the text checks, live in its Appendix A); **UD-C##** are CI conformance obligations, not runtime checks.

---

# Part I — The model (Core surface)

## I.1 The text authority pin

**All text in the engine — plain labels, rich paragraphs, math, text-on-path, and readouts — measures, shapes, and lays out through `ITypesetter`, then draws through the Tier-1 `DrawText` primitive.** Nodes never shape. Contexts never shape.

This is *forced* before it is chosen: `TextNode` and friends live in **Najm.Lib**, which references Core only — no SkiaSharp, no HarfBuzzSharp. The only text-shaped things Najm.Lib can touch are a Core capability and a Core draw op. It is then also *wanted*: one owner for the content-hash caches (§12.1), one determinism story (§2.2), one thing hosts inject, one seam the dvisvgm decorator wraps (architecture §12.5 and Part IV), and one environment-owned cache set that survives warm restart (NAJM-SKIA IV.9).

The **three-way split** mirrors the compositor boundary: portable model, production pipeline, and backend lowering.

| Concern | Owner |
|---|---|
| **Model** — `ITypesetter`, `TypesetRequest`, `ITextLayout` + run vocabulary, `FontFace`, `FontFamily`, `Style`, `RichContent`, markup grammar, fragment model, `PathSpec`/`PathPlacement` | **Core** (`Najm.Core.Text`) |
| **Production** — parsing, style resolution, itemization, shaping, math, line layout, fragments, on-path mapping, all caches | **Najm.Text** (`Najm.Text.Typesetter : ITypesetter`) |
| **Lowering** — blobs, glyph paths, paints, export behavior | **Najm.Skia** (NAJM-SKIA I.8, II.3, III.5, Appendix A) |

`NullTypesetter` (Core) throws on every typesetting call and names `HostOptions.Typesetter` and `Najm.Text` as the fix. A host that shows *any* text injects the real typesetter; `Najm.App` does so by default.

## I.2 Fonts and families

```csharp
// Core — immutable, backend-neutral face handle
public sealed class FontFace
{
 public string SourceId { get; }
 public ReadOnlyMemory<byte> Bytes { get; }
 public int FaceIndex { get; }
}
```

`FontFace` is size-independent. Sizes belong to typeset and draw requests. The retained bytes and face index are the portability floor.

Native realizations are never stored on the handle. `Najm.Text` owns HarfBuzz face/font side tables; `Najm.Skia` independently owns `FontFace → SKTypeface`. Both are environment-lifetime, keyed by the same portable handle, and disposed by their owning module.

```csharp
public sealed class FontFamily
{
 public required string Name;
 public required IReadOnlyDictionary<(FontWeight, FontSlant), FontFace> Faces;
 public IReadOnlyList<string> Fallbacks { get; init; } = [];
 public FontFace? MathFace { get; init; }
}
```

Latin Modern Roman and Latin Modern Math are bundled defaults. Unknown families fail during environment-aware style resolution. Face matching is deterministic and included in cache keys. Metrics are returned in local units with positive ascent/descent magnitudes.

## I.3 `ITypesetter`

```csharp
public interface ITypesetter
{
 void RegisterFamily(FontFamily family);
 void SetDefaultFamilies(string textFamily, string mathFamily);
 FontMetrics Metrics(FontFace face, float size);

 ITextLayout Typeset(in TypesetRequest request); // pure; content-hash cached (I.13)

 // Text-on-path second stage (I.11): refills a node-owned placement from a cached flat layout.
 void Place(ITextLayout flat, in PathSpec path, PathPlacement placement);
}

public readonly struct TypesetRequest
{
 public required RichContent Content; // plain text and lone math are degenerate RichContents
 public required Style BaseStyle; // cascade root; must resolve a family and a size
 public float? MaxWidth; // local units; null ⇒ single line (hard \n only)
 public TextAlign Align; // Left (default) | Center | Right — Justify is deferred
 public float LineSpacing; // leading multiplier; default 1.0
 public float ParagraphSpacing; // extra gap at blank-line breaks; default 0
 public string? Language; // request-default BCP-47 for shaping; spans override
 public bool Dynamic; // readout hint: node-exclusive mutable layout (I.12, II.9)
}
```

Deliberate absences, both cache-motivated:

- **No anchor.** Anchoring is a node-side offset over the finished layout (I.9). Putting it here would split the cache twelve ways for identical geometry.
- **No path.** On-path placement is a second, cheap, node-owned stage (I.11). Putting `PathOffset` here would turn every slide-along-the-path tween into a per-frame re-typeset.

`Typeset` is **pure compute** — legal in `OnLoad`, `Update`, and coroutines alike (fonts were loaded through `IAssets`; nothing here touches I/O — except Full-flavor math, which Part IV confines to load time). The first typeset of new content is a **content transition**; steady state re-reads cached handles.

## I.4 `ITextLayout` and run model

Layouts are immutable portable data. They contain geometry, cluster/source maps, paint-table values, fragment metadata, and backend-neutral runs.

```csharp
public interface ITextLayout
{
 Rect LogicalBounds { get; }
 Rect InkBounds { get; }
 int LineCount { get; }
 LineMetrics Line(int index);

 ReadOnlySpan<ushort> Glyphs { get; }
 ReadOnlySpan<Vector2> Positions { get; }
 ReadOnlySpan<int> Clusters { get; }
 ReadOnlySpan<GlyphRun> Runs { get; }
 ReadOnlySpan<RuleRun> Rules { get; }
 ReadOnlySpan<VectorPictureRun> Pictures { get; }
 ReadOnlySpan<Color> PaintTable { get; }

 FragmentTable Fragments { get; }
 long Generation { get; }

 float IndexToX(int line, int sourceIndex);
 int XToIndex(int line, float x);
 IPath BakePath();
}

public sealed class GlyphRun
{
 public required FontFace Font;
 public required float Size;
 public int PaintIndex;
 public int Start, Count;
 public byte Flags;
}

public readonly struct RuleRun
{
 public Rect Rect { get; init; }
 public int PaintIndex { get; init; }
}

public sealed class VectorPictureRun
{
 public required VectorPicture Picture;
 public Rect Bounds;
 public Vector2 BaselineOrigin;
}
```

`VectorPicture` is an immutable retained list of portable Tier-1 path, rule, transform, clip, and image commands. Backends may cache a native display list privately. Layouts never contain `SKTextBlob`, `SKPath`, backend-native picture objects, or writable native-object slots.

The layout origin is the first line's baseline pen at the alignment box's left edge; +x follows the baseline and +y points toward descenders. Cached layouts and their arrays remain immutable after construction.

## I.5 Styles and the cascade

```csharp
public struct Style // every property optional; unset = inherit through the cascade
{
 public string? Family;
 public FontWeight? Weight; // 100–900; named constants Thin…Black
 public FontSlant? Slant; // Upright | Italic | Oblique
 public float? Size; // absolute, local units
 public float? SizeScale; // multiplicative; composes down the cascade
 public Color? Color;
 public float? LetterSpacing; // local units; applied post-shape (II.5)
 public float? BaselineShiftFactor; // × resolved size (superscript ≈ +0.35)
 public bool? Underline, Strikethrough;
 public FontFeatures? Features; // Ligatures (default on) | SmallCaps | TabularNumbers | OldstyleNums + RawTags[]
 public string? Lang; // BCP-47; feeds shaping
 public FragmentTag? Tag; // immutable stable identifier for fragment queries
}
```

The property set is closed; extending it is a deliberate model/API change. **Cascade rule:** property-wise; node base style → outer spans → inner spans; innermost set value wins; `SizeScale` factors *multiply* through and compose with an inner absolute `Size` reset. Resolution happens **once at typeset** into a resolved-style table; runs carry indices, not styles.

**Palette.** `StylePalette` maps names → `Color` and names → `Style` for markup's `<c=…>` / `<style=…>`. It is a node property, defaulted from `ThemeAmbient` at attach. Syntax parsing records symbolic names; palette and family values resolve at attach or first typeset and fail loudly there when unknown. Palette identity participates in the layout cache key only through the resolved values — two palettes that resolve a string identically share cache entries.

## I.6 The markup grammar

One terse, closed grammar for author strings on `RichTextNode` and `TextOnPathNode`. `TextNode.Text` is plain by definition; `TexNode.Latex` is math by definition — one grammar plus two typed escapes.

```
markup := (text | escape | math | tag | closer | newline)*
math := '$' tex '$' | '$$' tex '$$' ; tex = raw LaTeX math, terminated by the matching $;
 ; \$ inside tex is a literal dollar
tag := '<' name ('=' value)? '>' ; name ∈ { b i u s c size f style lang tag }
closer := '</>' ; closes the innermost open tag
escape := '\$' | '\<' | '\\' ; any other '\x' passes through literally
newline := '\n' ; hard line break; a blank line is a paragraph break
```

| Tag | Meaning | Value |
|---|---|---|
| `<b>` `<i>` `<u>` `<s>` | weight 700 / italic / underline / strikethrough | — |
| `<c=…>` | color | palette name or `#rrggbb[aa]` |
| `<size=…>` | `SizeScale` factor | float (`<size=1.4>`); absolute sizes are API territory |
| `<f=…>` | family | registered family name |
| `<style=…>` | named palette style (merged as a span) | palette name |
| `<lang=…>` | language | BCP-47 |
| `<tag=…>` | `Style.Tag` (string) | anything |

Rules: tags nest; `</>` closes the innermost tag. Unclosed or unknown tags, malformed literal values, and unterminated `$` fail at property set with a character position. Environment-dependent names are resolved later as described above. `$…$` = inline math (text style); `$$…$$` = display style. Markup is a convenience layer that **lowers to the `RichContent` span model — the model is the API**: `RichContent.Parse(markup)` and `RichContent.Build(b => b.Text("E = ").Math("mc^2").Styled(new Style { Color = accent }, b2 => …))` produce identical trees; agents and tooling may build spans directly. `MathFlavor.Full` is builder/`TexNode`-only — deliberately absent from markup.

**Example:** `new TextOnPathNode(@"$\oint \vec E \cdot d\vec A = Q/\varepsilon_0$", wave) { Size = 42 }`.

## I.7 Fragments

Fragments map source selectors and optional `FragmentTag` values to layout clusters or math atoms. The cached layout owns immutable fragment geometry; each text node owns mutable overlay state for color, opacity, and transform.

```csharp
public readonly record struct FragmentTag(string Value);

public readonly struct FragmentHandle
{
 internal TextNode Node { get; }
 internal FragmentSelector Selector { get; }
 internal long LayoutGeneration { get; }
}
```

A handle resolves its selector against the node's current layout. If an incompatible content/layout change advances the generation and the selector no longer resolves identically, use fails clearly with an invalidated-handle error. Handles therefore never silently address different leaves after wrapping, fallback, or content edits. Tags and source ranges can be reacquired after relayout.

Overlay animation is node-owned and can be allocation-free after capacity is established. `PickFragment(localPoint)` returns the resolved fragment/tag for interaction.

## I.8 The draw primitive

```csharp
// Tier 1 — abstract on DrawContext2DBase; every backend lowers natively.
// Core has no glyph rasterizer, so there is no portable fallback.
void DrawText(ITextLayout layout);
void DrawText(ITextLayout layout, ReadOnlySpan<FragmentOverlay> overlays, Color? colorOverride = null);
void DrawText(ITextLayout layout, in PathPlacement placement,
 ReadOnlySpan<FragmentOverlay> overlays = default, Color? colorOverride = null);
```

`FragmentOverlay` is the node-table row (leaf range + transform + opacity + color), passed as a pooled span — zero-alloc at call sites. Application point: **between the node's transform and the glyph draw**, per run — overlays compose under the node transform, so a rotated theorem card rotates its highlighted fragment with it. `RuleRun`s follow their runs' overlay state; `VectorPictureRun`s are atomic (whole-item overlays only). The Skia lowering — blob strategy, RSXform runs, glyph-path export route, and mini-blob readouts — is specified in NAJM-SKIA II.3; export fidelity is specified in NAJM-SKIA III.5.

## I.9 Anchors, bounds, and the upright rule

```csharp
public enum TextAnchor
{
 BaselineLeft /* default */, BaselineCenter, BaselineRight,
 TopLeft, TopCenter, TopRight,
 CenterLeft, Center, CenterRight,
 BottomLeft, BottomCenter, BottomRight
}
```

- **Anchor is a node-side offset** over the finished layout — never a typeset input (cache purity, I.3). Baseline anchors reference the **first line's baseline**; box anchors reference `LogicalBounds`. Default **`BaselineLeft`**: tick labels sit naturally on their baselines and mixed-size labels align without ascent arithmetic.
- **`LocalBounds`** = anchor-adjusted `LogicalBounds` ∪ active overlay boxes (I.7). **`InkBounds`** exposed beside it. `Baseline(int line)` returns baseline positions in node-local coordinates for alignment helpers.
- **The upright rule.** Fonts and Skia are Y-down; `WorldLayer2D` is Y-up with the flip living in cameras (§5) — naïvely, every glyph in a world layer renders mirrored. The pin: layouts are emitted in the reading frame (I.4); **text nodes compose the reading frame into local space so text reads upright under the layer's visual up**. Base `Layer` gains `virtual bool YAxisPointsUp` — `ScreenLayer` false, `WorldLayer2D` true, custom layers inherit or override; the node reads it at attach (a layer fact, not a camera fact — bounds stay camera-free by construction, §6.6). Consequences, stated plainly: in a world layer, ascenders extend toward **+y** and line 2 stacks toward **−y**; in a screen layer, the reverse; hit boxes and fragment boxes live in the same corrected frame; an author's own `Scale(1,−1)` still mirrors deliberately. Text-on-path inherits the rule through its normal convention (I.11).

## I.10 Node surfaces (Najm.Lib)

Four public nodes, one engine. All are ordinary `Drawable`s with real measured bounds (§6.6), all expose fragments and `PickFragment`, all obey the laziness and transition rules below.

| Node | Content property | Notes |
|---|---|---|
| `TextNode` | `Text` (plain string) | single style from node properties (`Family`, `Size`, `Weight`, `Italic`, `Color`, `LetterSpacing`, `Features`); `MaxWidth`, `Align`, `LineSpacing`; the readout APIs (I.12) |
| `TexNode` | `Latex` (math string) | display style default; `Flavor` (`Fast`/`Full`); `Size`, `Color`; **sugar over a one-math-item `RichContent`** — fragments work on formulas |
| `RichTextNode` | `Markup` (I.6) or `Content` (`RichContent`) | `BaseStyle`, `Palette`, `MaxWidth`, `Align`, `LineSpacing`, `ParagraphSpacing` |
| `TextOnPathNode` | `Markup`/`Content` + `Path` | `PathOffset`, `PathAlign`, `NormalOffset`, `Overflow` (I.11); `MaxWidth` forbidden |

- **Laziness.** Property writes set a dirty flag; the layout builds on the **first read** (bounds, render, query) — §4.1 benign memoization, so a five-property setup costs one typeset. First build of new content is a permitted **content transition**.
- **Size vs. Scale.** `Size` is a typesetting input and a cache-key component; the documented idiom for grow/shrink animation is **`Transform.Scale`** — vector-crisp, visually identical, zero relayout. `Size` tweens work but re-typeset per frame; the §15 overlay's transition counter makes the mistake visible.
- **Color.** Uniform node `Color` is the draw-time override (I.4) — tween-safe.
- Anchors per I.9; the upright mapping per the node's layer.

## I.11 Text-on-path

The §12.2.5 contract, C.3-proof and animation-first:

```csharp
public readonly struct PathSpec
{
 public required IPath Path; // portable baked geometry; arc tables via II.10
 public float PathOffset; // arc length, local units; the animatable slide
 public PathAlign Align; // Start (default) | Center | End of residual length
 public float NormalOffset; // along the visual-frame normal (+90° from tangent)
 public PathOverflow Overflow; // ContinueTangent (default) | Clip
}

public sealed class PathPlacement // node-owned, pooled; refilled by ITypesetter.Place
{
 // per placement atom: position, rotation, glyph range — arrays resized on content change only
}
```

- **Placement atoms:** shaped **clusters** for text (ligatures rotate as one — Arabic joins survive the curve); **math placement atoms** for math (base + attachments rigid; fractions/radicals rigid). Anchor = the atom's **advance center on the baseline** mapped to arc length; rotation = the path tangent; "up" = +90° from the tangent in the layer's visual frame (upright rule); `NormalOffset` displaces along that normal.
- **Two stages, by design.** The *flat* layout is a normal `Typeset` product — cached, shared, path-free. The node owns a `PathPlacement`; `ITypesetter.Place(flat, in spec, placement)` refills it — pure arithmetic against the typesetter's cached per-path arc-length table, **zero steady-state allocation**. Animating `PathOffset` is therefore a per-frame `Place`, cost ≈ atoms × one arc-table lookup (~a hundred for C.3's banner), and **never a relayout**. Closed paths wrap `PathOffset` modulo length — marquees are one tween.
- **Overflow** on open paths: `ContinueTangent` (default — atoms past the end continue along the end tangent, predictable) or `Clip` (atoms past the end are skipped). **`MaxWidth` with a path is a fail-loud configuration error** — wrapping and path layout are mutually exclusive by construction.
- **Bounds** = union of placed atom boxes (computed in `Place`, camera-free); **hit/`PickFragment`** test per-atom oriented boxes — fragments work on curved text too.

## I.12 Dynamic text (readouts)

Dynamic numeric content uses a node-exclusive mutable layout with fixed capacity rather than mutating a shared cached layout. Formatting defaults to `CultureInfo.InvariantCulture`; an explicit culture may be supplied when locale-specific output is intended.

The dynamic cache is separate from whole-run shaping caches: digits, signs, separators, exponent markers, and known clusters are shaped once per face/style, then assembled in place. Capacity growth is a transition allocation; steady updates rewrite glyph/position arrays and reuse backend mini-blob caches.

## I.13 Determinism, caching, and restart

| Cache | Owner | Key | Lifetime / trim |
|---|---|---|---|
| Layout cache | typesetter | content hash: canonical `RichContent` + resolved base style + constraints | environment; epoch-trim (NAJM-SKIA I.5 pattern) |
| Shaped-run cache | typesetter | (face, font-unit text hash, script, direction, lang, features) | environment; epoch-trim |
| Arc-length tables | typesetter | `IPath` handle identity | environment; rebuilt on dynamic-path change (II.10) |
| Math parse (AST) | inside layout entries | — | rides the layout cache |
| HB face/font side tables | typesetter | `FontFace` handle | environment |
| Glyph-path cache | Najm.Skia | (face, glyph id) | environment; epoch-trim |
| Mini-blob cache | Najm.Skia | (face, size, glyph id) | environment; epoch-trim |
| Full-TeX disk cache | Part IV | (content, preamble, distro stamp) | persistent on disk |

- **Trim honesty:** trimming an entry a node still references is harmless — the node's strong handle keeps the layout alive; only future dedup is lost. Nodes never re-request per frame.
- **Warm restart (NAJM-SKIA S3):** every environment-lifetime cache above survives scene stop/start — a reloaded scene re-attaches to hot layouts; only node-owned state (overlays, placements) rebuilds, by design.
- **Determinism:** shaping is deterministic per HarfBuzz version + font bytes; CSharpMath per its version; layout arithmetic is ordered float math under the engine's global determinism posture. The pinned-environment fine print now names HB, CSharpMath, UCD, and font bytes; Full flavor adds the TeX distro stamp. Cross-platform bit-identity is *not* promised (same posture as Skia SIMD in §2.2); per-environment reproducibility is.

---

# Part II — The pipeline (Najm.Text realization)

## II.1 Anatomy of the typesetter

`Najm.Text.Typesetter : ITypesetter` owns, in construction order: the family registry (+ embedded LM defaults), the Unicode tables (II.11, static), the HB side tables (lazy per face), the CSharpMath bridge (lazy, II.6), and the caches of I.13. It is **render-thread affine** like every other engine service (§3): all calls arrive from the frame thread or load phase; no internal locking. One instance per environment; `SceneNode` children reach it through the wrapped environment (decorator-compatible, Part IV).

The `Typeset` pipeline, stage by stage:

```
RichContent ──II.3 style resolution──▶ styled item stream
 ──II.4 itemization──────▶ shaping items (font, script, dir, lang, features)
 ──II.5 HB shaping───────▶ shaped runs (font-unit glyphs+advances, cached)
 ──II.6 math adapter─────▶ math item boxes (glyph runs + rules | picture)
 ──II.7 line layout──────▶ lines, positions scaled to size, alignment
 ──II.8 fragment table───▶ leaves, run map
 ──────────────────────▶ ITextLayout (immutable, cached)
```

## II.2 Markup parsing

`RichContent.Parse` is a single forward pass, zero regex, zero intermediate strings: a span cursor, a tag stack, and an output builder of items — `TextItem { text-slice, style-span-chain }` and `MathItem { latex-slice, display, flavor = Fast }`. `$`/`$$` scanning respects `\$`; tag values parse by shape (`#` → hex color, digits → factor, else palette/family/lang lookup). Every error carries the character position and the offending token; errors throw at property set — author strings are load-ish-time, and silent best-effort markup is how documents rot. The builder API produces the same item stream directly.

## II.3 Style resolution

A fold over the span chains: start from `BaseStyle` (which must resolve family + size — else fail loud), apply outer→inner, factors multiplying, palette names already resolved at parse. Output: a **resolved-style table** (deduplicated; items carry indices), the **paint table** (colors per resolved style), and per-item HB inputs — face (family + weight/slant matched per I.2), size, feature array (`liga` default-on, `smcp`/`tnum`/`onum` per flags, raw tags appended), language. Underline/strikethrough flags survive to II.7, where they become `RuleRun`s from `FontMetrics` positions.

## II.4 Itemization

Full multilingual itemization:

1. **Paragraph split** on blank lines; per paragraph:
2. **UAX #9 BiDi** — an in-house implementation of the rule chain (P2–P3 paragraph level, X1–X10 explicit codes, W1–W7 weak, N0–N2 neutral, I1–I2 implicit), producing embedding levels per character and verified against UCD `BidiTest.txt` + `BidiCharacterTest.txt` (UD-C1/C2). Keeping this contained algorithm in `Najm.Text` avoids adding a broad runtime globalization dependency.
3. **Script itemization** — UAX #24 script property runs, with Common/Inherited resolved to the surrounding script.
4. **Font fallback** — per run, probe the styled face's cmap; uncovered clusters fall down the family's `Fallbacks` chain; the run splits at coverage boundaries. Before fallback support, uncovered characters shape to `.notdef` with a **one-time** debug log naming face and codepoint (deterministic output, loud diagnosis).
5. **Merge with style boundaries** → the final shaping items: maximal runs of (face, script, direction, language, features, size-class).

**Initial Latin-oriented path:** one item per text stretch — LTR, paragraph script, styled face, no fallback. The point of shipping HB shaping *inside* this degenerate path is metric stability: kerning and ligatures are correct from day one, so later multilingual itemization changes which runs exist, never where a Latin label's glyphs sit.

Reordering: shaping consumes logical order per item with the item's direction; **visual reordering of runs within a line** applies at line layout (II.7) from the UAX #9 BiDi levels. RTL runs' cluster maps stay monotone in logical order (caret sanity, HB-R4).

## II.5 Shaping (HarfBuzz)

- **Font-unit shaping.** The side-table `Font` per face carries `scale = (upem, upem)`, no hinting anywhere in the stack: shaped advances/offsets are size-independent, so **one shaped-run cache serves every size** — the entry scales by `size/upem` at line layout. This is the single highest-leverage cache decision in the stack.
- **Buffers are pooled** — a small stack of `HarfBuzzSharp.Buffer`s, `ClearContents` between uses (HB-R3); shaping allocates nothing per call beyond the cache entry on miss.
- Inputs per item: UTF-16 slice, direction, script, language, features. Outputs: glyph ids, advances, offsets, **cluster values** (→ `Clusters`, fragments, caret).
- **Shaped-run cache** key: (face, hash of the font-unit-relevant inputs: text slice, script, direction, lang, features). Entries are the raw HB output arrays, immutable.
- **LetterSpacing** applies **post-shape**: added to advances at cluster boundaries only (never inside a ligature/cluster — the standard rule); the documented caveat that large tracking with ligatures enabled reads oddly is the author's tradeoff, with `Features.Ligatures = false` the escape.
- The **checks**: HB-R1 golden-shapes a kern pair and the `fi` ligature against golden outputs for the bundled faces (catches a broken native or a font-substitution surprise); failure is fail-loud — there is no correct fallback for wrong shaping.

## II.6 The math adapter (CSharpMath, Fast flavor)

The adapter relies on three pinned API facts: CSharpMath exposes its display tree (`MathPainter.Display`; `Typesetter.CreateLine(mathList, fonts, context, style)` is directly callable); it parses via `LaTeXParser` into a walkable `MathList` AST; and its fonts are **Typography-library typefaces over font files** — glyph ids in its displays are *font-file* facts.

- **The font bridge (CS-R1).** Feed CSharpMath the **same bytes** our `FontFace` math face wraps (a `Fonts` instance over a local typeface list constructed from `FontFace.Bytes`). Glyph-id identity then holds *by same-file construction*: a glyph id in a CSharpMath display is valid against our face, our HB tables, our glyph-path cache. For the bundled default this is trivially true — Latin Modern Math is both our embedded math face and CSharpMath's native default.
- **Parse:** `LaTeXParser.MathListFromLaTeX(latex)` → AST; errors fail loud with CSharpMath's message and the item's source position. The AST is retained in the layout entry — it is the target of structural `Find` needles (CS-R4: parse the needle, match sub-`MathList`s by structural equality, atom paths → leaf ranges).
- **Typeset:** `Typesetter.CreateLine(ast, bridgedFonts, TypesettingContext.Instance, lineStyle)` with `lineStyle` mapped from Najm's defaults (`$…$` → text style, `$$…$$`/TexNode → display). CSharpMath works in font-size points ≡ our local units at the item's resolved size — positions map 1:1.
- **The structured walk (CS-R2/R3):** recursively enumerate displays — glyph-bearing displays (`TextLineDisplay`/`GlyphDisplay`/constructed large operators) → `GlyphRun` items against the bridged face; line/rule displays (fraction bars, radical overbars) → `RuleRun`s; positions are baseline-relative pen coordinates folded into the layout's reading frame. Along the way, **placement-atom decomposition**: a base glyph plus its attached accents/scripts forms one atom; fractions/radicals/delimited groups are one atom — the leaf table (II.8) and text-on-path (I.11) both consume this.
- **Color/size** come from the enclosing span's resolved style; `\text{…}` inside math renders with the math font's text variant — a **current limitation** relative to the surrounding text family, unified when a real need appears (App. A).
- **Fallback:** if a CSharpMath extraction check fails, render the item once through the painter into a portable image command inside `VectorPictureRun`, preserving measured bounds and baseline. The result remains correct and cacheable but vector export contains an explicitly reported raster unit, and fragments/text-on-path coarsen to the whole item. The baseline implementation is expected to pass the structured-walk checks for bundled fonts; fallback is an environment-safety path, not the normal representation.

## II.7 Line layout

- **Break opportunities:** the initial slice supports hard `\n` only. The rich-text stage adds a **UAX #14** greedy breaker (UD-C3-conformant); Knuth–Plass and hyphenation are **deferred** (figure text is short; the seam is the breaker function). Math items are **atomic** — no internal breaks (current limitation).
- **Assembly:** per line, place items along the baseline pen (advances scaled `size/upem`), apply BiDi visual reordering (II.4), then alignment (`Left`/`Center`/`Right` against `MaxWidth` or natural width; `Justify` deferred, I.3).
- **Baseline unification (§12.2.2):** every item sits **baseline-on-baseline** — CSharpMath displays are already baseline-relative, so the adapter's pen mapping *is* the unification. Line ascent = max over items (text ascents from `FontMetrics`, math heights from the adapter); descent likewise. A tall inline fraction deepens its line and only its line.
- **Leading:** baseline-to-baseline = dominant style's (ascent + descent + lineGap) × `LineSpacing`; a blank-line paragraph break adds `ParagraphSpacing`. `BaselineShiftFactor` offsets an item's pen vertically by factor × size (superscripts without math mode).
- **Decorations:** underline/strikethrough spans emit merged `RuleRun`s per line from `FontMetrics` positions/thicknesses — vector-native everywhere.
- Outputs: pooled glyph/position/cluster arrays, run views, rules, pictures, line metrics, logical/ink bounds — the immutable layout.

## II.8 The fragment table and run splits

- **Leaves** are enumerated during II.5/II.6: text leaves from cluster boundaries (box = advance × line vertical metrics, at the cluster's pen), math leaves from the adapter's atoms. Each leaf records `(SourceStart, SourceLength, Box, Run, GlyphStart, GlyphCount, Line, IsMath, Tag)` — the I.7 struct — into one immutable array, in visual order with a source-order index alongside (both query directions are O(log n)).
- **Run splits on overlay attach:** an overlay covering `[leafA..leafB]` requires draw-time isolation of that glyph range. Because runs are *views* (I.4), the split materializes **new run headers** at the leaf boundaries — the pooled arrays are untouched; blob slots on the affected originals are dropped (their blobs no longer match a drawable range) and rebuilt lazily by the backend. Split sets are cached on the node keyed by the overlay's leaf range: attach = one small transition; detach restores the original run list; **animating overlay parameters touches nothing here** (I.7's pin, honored).

## II.9 The dynamic path (readouts)

- `SetText`/`SetValue` (I.12) target a node-exclusive **mutable** layout (`Dynamic = true` requests bypass the shared cache — the one legal mutation in the stack, invisible to sharing by construction).
- **The formatter:** a fixed-format `double` → chars writer into the pooled buffer (`"0.000"`-class formats: sign, integral digits, point, fixed fraction — covers readouts; `G`-format falls back to `double.TryFormat`, still allocation-free). No `string`, no culture lookup per frame (invariant digits pinned).
- **Change detection:** hash-compare the buffer against current content; identical frames do nothing.
- **In-place relayout:** single-run path — cmap/shape via the shaped-run cache (digit-heavy content hits per-cluster entries; `tnum` makes digit advances uniform so positions are arithmetic), rewrite glyphs/positions into the existing arrays (grow = rare capacity transition), update bounds. No new layout object, no new arrays in steady state.
- **Drawing** never builds per-frame blobs: the dynamic flag routes through Skia's **mini-blob cache** — one cached single-glyph blob per (face, size, glyph), ~ten draws per readout. Steady-state readout frames are **allocation-silent** under the §15 GC canary — that is the acceptance test (App. C).

## II.10 On-path mapping

- **Arc tables:** each portable `IPath` is flattened through the Core path-geometry API into deterministic contour spans and cumulative arc lengths. The table is cached by path-handle identity; rebuilding a path produces a new handle and a new table.
- **Placement:** for each cluster or math atom, compute its arc coordinate from the pen center, `PathOffset`, and alignment shift; sample position and tangent; apply visual-frame normal offset; and write position/rotation directly into node-owned pooled arrays. Closed contours wrap, while open contours follow the configured overflow policy.
- RTL and mixed-direction runs arrive in visual order from line layout, so the placement stage remains direction-agnostic.

## II.11 Unicode data

A build-time generator consumes the pinned UCD and emits **compact two-stage trie tables** as source: `Bidi_Class`, `Script`, `Line_Break`, `Grapheme_Cluster_Break` (grapheme tables may be generated before their editing consumers ship). Lookups are branch-light array indexing — allocation-free, WASM-friendly (no native data files to locate). The UCD version is stamped into the generated source and reported by `--diag`; conformance suites (UD-C1..C4: BidiTest, BidiCharacterTest, LineBreakTest, GraphemeBreakTest) run in CI against the same pin. Regenerating against a new UCD is a deliberate versioned-data update (it can move line breaks — observable).

## II.12 Cache realization and memory notes

Typesetter caches are environment-owned dictionaries with epoch/LRU trimming and diagnostics for layout count, shaped-run count, arc tables, and estimated managed bytes. `Najm.Text` owns HarfBuzz and layout caches; `Najm.Skia` owns typeface, blob, glyph-path, and native-picture caches. No cache mutates portable handles or layouts.

Static text performs no typesetter work during steady-state rendering. New content, changed capacity, and first backend realization are transition costs reported by diagnostics.

# Part III — Backend lowering

Backend lowering is owned by the backend companion. The Skia path uses private side tables for typefaces, blobs, glyph outlines, and `VectorPicture` realizations. Flat runs normally draw through positioned blobs; text-on-path may use RSXform blobs or a correct per-atom fallback. Vector targets emit outlines and portable picture commands. Fragment overlays wrap already-split runs with transforms and paint modulation.

Text remains ordinary node content to the compositor. Fragment opacity is paint modulation within the text drawable; node-level opacity/effects still follow the general composition atomicity rules.

# Part IV — The dvisvgm tier (Full flavor)

Publication-grade TeX — packages, custom preambles, real `\physics`-style macros — without dragging a TeX runtime into any frame.

- **`DviSvgmTypesetter` is a decorator**: wraps any `ITypesetter`, intercepts `MathFlavor.Full` items, delegates everything else — text, rich layout, Fast math — to the inner. It *is* an `ITypesetter`, so it drops into `HostOptions.Typesetter` or a per-scene `env.With(...)`; §12.2.6's "alternative ITypesetter", literalized without forking the text stack.
- **Pipeline per Full item:** write `item.tex` = pinned document class + `\usepackage[active,tightpage]{preview}` + author preamble (a `TexNode.Preamble`/environment option) + the math; run `latex` → DVI; run `dvisvgm --no-fonts` (glyphs as paths) → SVG; parse the SVG into a portable `VectorPicture` plus tight bounds; **baseline** from the preview package's depth reporting, giving inline-correct Full items. The result enters the layout as a `VectorPictureRun` — vector on export by construction, **no sub-math fragments** (an explicit Full-flavor limitation).
- **Disk cache:** `(contentHash, preambleHash, distroStamp) → svg + metrics sidecar` under the project cache dir; the **distro stamp** hashes `latex --version` + `dvisvgm --version`. Cache hits never launch a process — a warm project builds Full figures with zero toolchain latency.
- **Load-time confinement (§4.4):** Full items resolve during the load phase — nodes carrying Full content typeset eagerly at attach; a Full item first encountered outside load (e.g. assigned from `Update`) **fails loud** naming the rule. A Fast-only typesetter hitting a Full item fails loud naming the decorator.
- **Failure modes:** missing tools → fail loud with install hint; TeX compile error → fail loud with the log tail and the item's source position. Never a silent blank box.
- Determinism: pinned distro (the stamp makes drift visible), pinned preamble, cached artifacts — a Full figure re-exports byte-comparably on the same environment.

---

# Part V — Interaction

- **Hit:** node `HitTest` = union of line boxes; oriented atom boxes for on-path (I.11). Hit-testing is bounds-in, reverse-paint-order as everywhere (§9); text adds nothing to the walk.
- **`PickFragment(local)`** on all text nodes (I.7) — the element-level pick. The **links recipe**: mark a span `<tag=doc:intro>`; on click, `PickFragment(e.Local)?.Tag` dispatches. Hover highlighting = the same pick driving a fragment color/opacity overlay — no extra machinery.
- **Caret floor** (`IndexToX`/`XToIndex`, I.4): exact for single-style layouts via cluster maps and sufficient for a future `TextBox`. `TextBox` wires a `TextNode`, the caret floor, and rune input; edits are input-driven content transitions. The advanced-editing stage adds grapheme-correct motion through UAX #29, selection rendering, and IME composition without changing the basic layout surface.

---

# Part VI — Staging, performance, failure

## VI.1 Staging

Implementation order is governed by `ROADMAP.md`.

- **Baseline text slice:** bundled fonts, Latin-oriented HarfBuzz shaping, plain text, Fast math through portable vector pictures, measurement/baseline anchors, dynamic numeric readouts, and outline export.
- **Signature authoring features:** text-on-path, `BakePath`, and basic fragment overlays.
- **Demand-pulled typography:** rich markup/wrapping, full math-fragment decomposition, BiDi/script itemization/font fallback, then grapheme-aware editing and IME.
- **Offline publication tier:** the external dvisvgm decorator may land independently once figures require package-grade TeX.

Each stage must preserve the portable model; later shaping sophistication does not change node or backend contracts.

## VI.2 Performance posture

Steady-state frame: **zero typesetter work** for static text (nodes hold handles; contexts draw cached blobs); readouts cost one in-place relayout + ~10 mini-blob draws; on-path tweens cost one `Place` (~atoms × table sample). Transition costs (shape + layout + blob build) are bounded, visible on the §15 overlay's transition counter, and dominated by first-shape cache misses; a slide of new rich text lands in fractions of a millisecond of shaping on commodity hardware — but the number that matters is the *steady-state zero*, and the GC canary enforces it.

## VI.3 Failure table

| Condition | Behavior |
|---|---|
| Malformed markup / unterminated `$` | fail at property set, with position |
| Unknown family / palette / inherited style | fail at attach or first typeset, with position and resolved environment context |
| Math parse error (Fast) | fail loud with CSharpMath message + position |
| `MaxWidth` + `PathSpec` | fail loud (config error) |
| `Full` item, Fast-only typesetter | fail loud naming `DviSvgmTypesetter` |
| `Full` item outside load phase | fail loud naming the load-time rule (Part IV) |
| Missing TeX toolchain / compile error | fail loud with hint / log tail (Part IV) |
| Missing glyph before fallback support | `.notdef` + one-time log (deterministic degrade) |
| Bitmap/COLR face on vector export | unit rasterizes; SK-R14 names the face |
| CSharpMath adapter check failure | math demotes to a whole-item `VectorPictureRun`, possibly with a reported raster image command; warning once |
| SK-R13 failure | on-path falls back to per-atom draws (correct, slower) — warning once |

The split follows the house rule: **author mistakes fail loud; environment shortfalls degrade correctly and say so.**

---

# Appendix A — Check registry

Discipline per §0: first-use, cached verdict, stated fallback, loud log. SkiaSharp checks **SK-R13–R15** live in NAJM-SKIA Appendix A.

**HarfBuzzSharp**

| Check | Asserts | On failure |
|---|---|---|
| HB-R1 | Golden shape of a kern pair + `fi` ligature against expected outputs for the bundled LM faces | fail loud — no correct fallback for wrong shaping |
| HB-R2 | `ot-metrics` yields underline/strikeout position+thickness for the bundled faces | fall back to OS/2-derived values via the native realization typeface's metrics; log once |
| HB-R3 | Pooled `Buffer.ClearContents` preserves capacity (no per-shape realloc) | keep pooling but log the perf regression once |
| HB-R4 | Cluster values monotone in logical order for an RTL sample | caret mapping degrades to run-start granularity in affected runs; log once |

**CSharpMath**

| Check | Asserts | On failure |
|---|---|---|
| CS-R1 | `Fonts` constructed over caller-supplied bytes; glyph id of a probe char equals our HB cmap lookup for the same file | demote math to VectorPictureRun (bridge unsound) |
| CS-R2 | Display tree reachable: `Typesetter.CreateLine` returns walkable displays | demote to VectorPictureRun |
| CS-R3 | Adapter extraction of a golden formula matches `MathPainter` raster within AA tolerance | demote to VectorPictureRun |
| CS-R4 | Structural needle match on `MathList` finds `x^2` in a golden AST | fragment `Find` on math disabled (whole-item handles remain); log once |
| CS-R5 | Adapter bounds/baseline equal painter measurement | demote to VectorPictureRun |

`VectorPictureRun` fallback is always **correct and export-honest** (II.6); it costs granularity and may introduce one explicitly reported raster image command.

**Unicode (CI obligations, not runtime)**: UD-C1/C2 — UAX #9 vs `BidiTest.txt`/`BidiCharacterTest.txt`; UD-C3 — UAX #14 vs `LineBreakTest.txt`; UD-C4 — UAX #29 vs `GraphemeBreakTest.txt`; all against the pinned UCD.

# Appendix B — Worked scenarios

**C.1 Tick labels.** Forty `TextNode`s, `"0.0" … "2.0"`, `Anchor = BaselineCenter`, pinned via `ScaleMode.Virtual`. Distinct strings → 21 layout-cache entries; duplicates share handles (dedup, I.3). Baselines sit on tick geometry because anchors are baseline-true (I.9). Pan/zoom: transforms only — zero typesetter work, blobs cached, GC canary silent. Recolor on hover: draw-time override (I.4) — still zero relayout.

**C.2 The readout.** `hud.Add(new TextNode { Anchor = BaselineLeft }.Bind(v => v.SetValue(sim.T, "0.000")))` — each frame: format into the pooled buffer (II.9), hash-compare, in-place relayout on change (digit advances uniform under `tnum`), draw via mini-blobs. Steady state: **zero allocation**, ~10 draws. The §15 canary stays flat — the acceptance test.

**C.3 The banner — Appendix B.3, traced.** `new TextOnPathNode(@"$\oint \vec E \cdot d\vec A = Q/\varepsilon_0$", wave) { Size = 42 }` inside `banner.Mask`. Parse: one display-math item (I.6). Adapter (II.6): CSharpMath AST → glyphs, accent atoms (`\vec E`), a scripted atom (`\varepsilon_0`) — a linear atom row on one baseline. Flat layout caches; `Place` maps atom centers to the wave's arc table, tangent-rotates, normal per the screen layer's visual up (II.10). Mask: the node renders normally; the mask consumes **alpha** (default channel) — glyph coverage gates the gradient ribbon; the glow then halos the masked result — exactly the constitution's described picture, with zero text-specific compositor machinery (Part III audit). Slide: tween `PathOffset` — per-frame `Place`, no relayout. PDF export: placements bake into per-atom transforms over glyph outlines; the mask realizes per SK-R12; output is pure vector.

**C.4 Theorem card.** `new RichTextNode { Markup = "<b>Theorem.</b> For all <c=accent>$x$</c>, $x^2 \\ge 0$.", MaxWidth = 480 }`. Cascade resolves spans (II.3); `$…$` items typeset inline and unify baselines — the `x²`'s height deepens only its line (II.7). `var frag = node.Find("x^2")` → structural AST match (CS-R4). Hover: `PickFragment` → same handle → `frag.Color = accent` (overlay attach = one split transition); pulse: `node.Animate(t => frag.Offset = new(0, -6 * Ease(t)))` — parameter animation, allocation-free; bounds extend upward with the slide, so culling and picking stay honest.

**C.5 Arabic caption.** `"النجم Najm 3.0"` — BiDi levels split RTL/LTR/digits (II.4); script runs shape Arabic with joining through HB; fallback covers the Latin stretch from the family chain; line layout reorders runs visually (II.7). On a path, atoms arrive in visual order and curve correctly (II.10). Before multilingual itemization, the same string renders `.notdef` boxes with one loud log — degraded, deterministic, diagnosable.

**C.6 Full-TeX figure.** `new TexNode { Latex = @"\qty{3.0e8}{\m\per\s}", Flavor = MathFlavor.Full, Preamble = @"\usepackage{siunitx}" }` under `DviSvgmTypesetter`. First load: latex → dvisvgm → SVG → picture + preview-depth baseline; disk-cached by (content, preamble, distro). Every later run: cache hit, no process. PDF export: picture replays vector. Under a Fast-only typesetter the same node fails loud naming the decorator.

**C.7 The upright rule, visibly.** The same `TexNode` in a `ScreenLayer` HUD and in a `WorldLayer2D` plot: both read upright; in the world layer its ascenders extend toward +y and `LocalBounds.Top > baseline` in world coordinates — a label "above" a point is `point + (0, h)`, exactly the math convention (I.9). The camera's flip never touches the node's bounds — they were corrected at the layer, not the camera.

**C.8 Equation morph (recipe, not machinery).** Two `TexNode`s, `a²+b²=c²` → `c²=a²+b²`: `Find` matching fragments on both; hide the target's, overlay-tween the source's `Offset` along world-space deltas computed from both nodes' fragment `Bounds` + transforms; crossfade opacities; swap visibility at the end. The engine supplies queries, overlays, and stable bounds; the correspondence solver is author/`Najm.Guard` territory — kept out of engine scope.

# Appendix C — Test obligations

1. Shaping goldens: kern pair, `fi` ligature (HB-R1 as a unit test); Arabic joining forms.
2. UD-C1..C4 conformance suites green against the pinned UCD.
3. Layout determinism: identical requests → identical handle (dedup) and byte-identical geometry across runs.
4. Anchor semantics: all twelve anchors against a golden box; baseline anchors baseline-true.
5. Upright rule: one glyph golden per layer kind; world-layer bounds sign test; author `Scale(1,−1)` still mirrors.
6. Baseline unification golden: inline fraction deepens exactly its line; math baseline == text baseline.
7. Wrap determinism: UAX #14 breaker on golden paragraphs; math items never split.
8. Cascade resolution: factor composition, innermost-wins, palette resolution; markup error positions.
9. Fragment overlay: attach = one transition (allocation asserted), animation = zero-alloc (canary test); bounds extend; `PickFragment` hits a slid-out fragment; detach restores runs.
10. Structural math `Find`: `x^2` in goldens; miss on `x^3` (CS-R4 as a unit test).
11. On-path: C.3 atom placement golden; `PathOffset` tween zero-alloc; closed-path wrap; `MaxWidth`+path throws; Clip vs ContinueTangent goldens.
12. Readout: `SetValue` steady-state zero-alloc under the GC canary; `tnum` width stability; capacity-growth counted as transition.
13. Color override tween: zero relayout (cache-entry count stable across a color animation).
14. `Size` tween: functions, and increments the transition counter (the documented anti-idiom is visible).
15. Vector export: PDF from a text scene contains **no font objects** (`/FontFile` absent — outlines only); bitmap-glyph face triggers the rasterize row; `BakePath` clip golden.
16. Full flavor: disk-cache hit skips the toolchain (process-launch counter); baseline matches preview depth; all Part IV failure modes fire loud.
17. Warm restart: layout/shaped-run/arc caches survive; node overlays reset; no re-shape on reload (cache-hit counters).
18. NullTypesetter throws naming the option; Fast-only typesetter on Full throws naming the decorator.
19. Hit = union of line boxes; reverse-paint order unaffected by text nodes.
20. Determinism smoke: full-scene text render hash stable across runs on one environment (§2.2 posture).

---
