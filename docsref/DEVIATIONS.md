# Deviations from the reference documentation

The reference set — `ARCHITECTURE.md`, `NAJM-SKIA.md`, `NAJM-TEXT.md`,
`NAJM-COMPOSITOR.md`, `ROADMAP.md` — is the design authority. `PLAN.md` records
execution order and its own "Specification resolutions" for questions the
architecture left open.

This file records the remainder: places where the implementation does something
the reference does not describe, does not specify, or specifies inconsistently.
Every entry states what the docs say, what the code does, and why. An entry is
added at the moment the decision is made, not retroactively.

Statuses: **Open** (decided, not yet implemented), **Implemented**,
**Superseded** (a later doc revision or decision replaced it).

---

## 1. `Scene` has no per-tick hook

**Docs:** §4.1 declares `Scene` with `OnLoad`, `OnStart`, `OnStop`, `OnUnload`
and no per-tick override. `Layer` gets `Update(in TickContext)` (§5.4, "runs
before the layer's tree"); `Node` and `Behavior` get `Update` as well. `Scene`
alone has nowhere to put per-frame logic.

**Decision:** add `protected virtual void Update(in TickContext tick)` to
`Scene`, invoked before the layer traversal.

**Why:** the intended authoring pattern is a `Scene` subclass, and in practice a
generic base scene derived into several concrete scenes. Without a scene-level
tick hook, per-frame logic has to be smuggled into a `Behavior` attached to a
node that exists only to host it. The name is `Update` rather than `OnUpdate`
because the codebase and the docs both use bare `Update` for the per-tick
override on `Node`, `Behavior`, and `Layer`; §3.1's `On`-prefix rule
disambiguates hooks from same-named commands (`Scene.OnStart` vs.
`Scene.Start(routine)`), and no `Scene.Update` command exists to collide with.

**Status:** Implemented, alongside the minimal scheduler. `Scene.Update` runs
before the layer traversal, ahead of the tween and coroutine passes. Both
physics samples drive their integrators from it and neither needed a host node
to carry a `Behavior`, which is the friction the entry predicted.

---

## 2. `Node.Render` is public, not protected

**Docs:** §6.1 lists `Render(ctx)` among `Node`'s lifecycle members without an
accessibility. §7.5 declares `public abstract override void Render(IDrawContext2D ctx)`
on `Drawable` — the `override` keyword requires a virtual `Render` on the base,
and `public` cannot widen a `protected` base member.

**Decision:** `Node.Render(IDrawContext2D)` is `public virtual`. `Update` stays
`protected virtual`.

**Why:** forced by the docs' own `Drawable` declaration. Noted here because it
makes `Render` and `Update` differ in accessibility, which looks like an
oversight when read cold.

**Status:** Implemented.

---

## 3. `Camera2D`'s world→virtual matrix

**Docs:** §5.2 states only that `Camera2D` is "a `Node2D`: Position/Zoom/Rotation;
`CenterOn`, `FitRect` helpers", that it maps "Y-up world → virtual" and that
"the Y flip lives here". §3.3 fixes virtual space as fixed-resolution, Y-down,
origin top-left. §5.2 also asserts the zoom≡crop invariant: "Camera zoom scales
geometry and every local-unit quantity together." No matrix, no statement of
which world point lands where, no numeric meaning for `Zoom`.

**Decision:**

- `Position` is the world point that lands at the center of virtual space
  (`VirtualResolution / 2`).
- `Zoom` is virtual units per world unit, default `1`. Larger `Zoom` draws
  larger.
- `Rotation` applies about `Position`.
- World `+Y` maps toward virtual `−Y`.

In the row-vector convention used throughout (`PLAN.md` resolution 2):

```
worldToVirtual = Translate(-Position) · Rotate(-Rotation) · Scale(Zoom, -Zoom) · Translate(virtualCenter)
```

**Why:** the zoom≡crop invariant fixes the *direction* of `Zoom` — "scales
geometry" means larger is bigger, which rules out framing-height semantics.
Given that, one world unit to one virtual unit at `Zoom = 1` is the only
fixpoint that does not introduce an arbitrary constant, and center-anchoring is
what makes `CenterOn` and `FitRect` express naturally.

**Status:** Implemented. The Y-flip sign was confirmed by mutation: replacing
`Scale(Zoom, -Zoom)` with `Scale(Zoom, Zoom)` fails exactly the sign-sensitive
tests and nothing else.

---

## 4. `SkiaExport.Png`

**Docs:** `SkiaExport` is specified with `Pdf` and `Svg` only (NAJM-SKIA V.2).
A single PNG frame has no convenience; it must go through
`SkiaOffline.Render` with a frame sink. There is no stated reason for the
omission.

**Decision:** add `SkiaExport.Png(Func<Scene> make, string path, double at, ...)`,
with the same `at:`-seeking semantics as `Pdf`/`Svg` — run `ceil(at · fps)`
ticks, then render once (§2.3).

**Why:** rendering one frame to inspect it is the most frequent action while
authoring a visual. Routing it through a multi-frame sink means computing a
frame count by hand and discarding all but the last file.

**Status:** Implemented, alongside the offline delivery slice. Every sample author
since has used it as their inspection loop, which is the behaviour the entry
predicted.

---

## 5. No portable seam for installing render scale and the base transform

**Docs:** NAJM-SKIA II.1 says the traverser's per-node transform is installed via
`canvas.SetMatrix(base × node)`, where base is `RenderScale` × camera mapping —
a Skia call. §5.3 step 2 requires the base transform be installed per layer.
But `IDrawContext2D` exposes no member that sets render scale, caps, or a base
transform, and §7.4 marks `PushTransform` as author-tier, composing "strictly
below" the engine transform. Today the only mechanism is
`SkiaRenderTarget.BeginPass(float, RenderCaps, in Matrix3x2)`, which is
`internal` to `Najm.Skia` and therefore invisible to Core.

As written, Core's traverser cannot drive any backend. The docs do not say
whether pass-begin belongs on `IRenderTarget`, on the backend-facing composition
SPI, or stays backend-internal.

**Decision:** the seam is portable and splits in two.

- `IRenderTarget.GetContext(float renderScale)` begins a clean pass at that
  scale, with the uniform scale installed as the pass's initial engine
  transform. The existing `GetContext()` becomes a default implementation
  meaning `GetContext(1f)`, so no implementor is forced to write it. A
  `renderScale` that is not finite and positive throws
  `ArgumentOutOfRangeException`.
- `IDrawContext2D.SetEngineTransform(in Matrix3x2 engineToDevice)` installs the
  already composed `renderScale × layerBase × nodeWorld` mapping. It is a set,
  not a push: it replaces the engine transform wholesale and leaves nothing to
  pop. Author `PushTransform`/`PushClip`/`PushOpacity` compose strictly below
  it, and it throws `InvalidOperationException` — naming the unbalanced kinds
  that remain — when the author stack is not empty.

`SkiaDrawContext2D` realizes both through the existing pass machinery:
`SetEngineTransform` reinstalls the matrix on the pass's own save slot above the
surface baseline, and `BeginPass(float, RenderCaps, in Matrix3x2)` stays
`internal` but now routes its base transform through the same private installer
rather than duplicating it, so the pass save count — and therefore `EndPass` —
is unchanged.

**Why:** the docs already establish that backend-facing SPI lives on the public
surface, marked as such in XML documentation, for the composition brackets; the
transform seam is the same kind of member for the same caller and belongs in the
same place rather than behind an `internal` a portable Core cannot reach. One
setter serves both paths: the composited path calls it per node, and the direct
path (§5.3) simply calls it again per layer with that layer's base, so no second
per-layer entry point is needed. Making the empty-stack requirement throw rather
than assert turns §7.4's "authors balance their pushes within `Render`" into a
structural guarantee the traverser can rely on in release builds, where an
unbalanced author push would otherwise silently trap the engine transform under
state the engine does not own.

**Status:** Implemented.

---

## 6. Exception types for "fail loud"

**Docs:** the house rule is stated repeatedly — "author mistakes fail loud;
environment shortfalls degrade correctly and say so" (NAJM-TEXT VI.3), "fail
loudly for unsupported author requests" (`PLAN.md`). Specific failures are
enumerated with required *message content* ("naming the option to set", "with
the character position", "naming the decorator"). But no document maps any
failure class to a .NET exception type, and no `NajmException` hierarchy is
specified.

**Decision:** follow the distribution the existing code already established —
`InvalidOperationException` for lifecycle and contract violations,
`ArgumentOutOfRangeException` / `ArgumentException` for parameter validation,
`InvalidDataException` for malformed asset or font bytes,
`NotSupportedException` for a capability the backend genuinely lacks. No custom
exception base type until something needs to catch Najm failures selectively.

**Why:** consistency with the 165-odd throw sites already in the tree is the
only signal available, and inventing a hierarchy now would be a public-surface
commitment made on no evidence.

**Status:** Implemented, in the sense that every slice since has followed it and
no case has yet wanted a Najm-specific exception type. Revisit only if something
needs to catch Najm failures selectively.

---

## 7. `Camera2D.FitRect` takes the virtual resolution

**Docs:** Appendix B.2 writes `layer.Camera.FitRect(Bars.GeometryBounds)` — one
argument. Fitting a rect requires knowing the viewport it must fit inside, and
that is `Scene.VirtualResolution`, which the camera does not own.

**Decision:** `FitRect(in Rect worldRect, in Vector2 virtualResolution)` for now.

**Why:** a one-argument form had no honest source for the viewport size when this
was decided; inventing a default would silently frame against the wrong box.

**Status:** Implemented. `WorldLayer2D.FitRect(in Rect worldRect)` now exists and
forwards to the two-argument form, so author code reads as the reference shows.
It forwards the layer's own framed extent rather than `Scene.VirtualResolution`
unconditionally, which is the correction this entry called for: a viewport'd
layer frames its viewport, and passing the scene's resolution would have framed
the wrong box in exactly the case the convenience is most useful.

---

## 8. Fast math preconditions the reference does not state

Two obligations found by executing the `CSharpMath.Rendering` spike (see the
resolved dependency conflict below). Neither appears in `NAJM-TEXT.md`, and both
produce output that renders and looks approximately right while being
measurably wrong — the failure mode a low-resolution golden waves through.

**CFF glyph bounds must be primed.** Latin Modern Math is a CFF/OTTO font, and
`OpenFontReader.Read()` leaves every glyph's `Bounds` at `[0,0,0,0]`. CSharpMath
measures through `GlyphBoundsProvider.GetBoundingRectsForGlyphs`, which reads
exactly those bounds, so handing it Najm's bytes unprimed yields plausible but
wrong metrics. For `\frac{a}{b}` the ascent came out 18.053 instead of 29.840;
for `\sqrt{\frac{x+1}{y^2}}` the radical selected a *different vertical variant*,
so the outline stream itself differed. It is deterministic, so it never presents
as flakiness.

The fix is one call per face at load: `typeface.UpdateAllCffGlyphBounds()`
(`Typography.OpenFont.Extensions`), about 32 ms for LM Math's 4802 glyphs. After
it, measurements and the full recorded command stream are byte-identical to
CSharpMath's own bundled-font path. This belongs in the `CS-R#` check registry
with a first-use assertion that a known glyph's bounds are non-zero, and its
cost belongs in the load-phase budget, never the frame path.

**The Y flip is Najm's to own.** `GraphicsContext` does not flip Y. Driving
`Typesetter.CreateLine` directly — the II.6 route — yields a mathematical,
up-positive axis: in `\frac{a}{b}` the numerator sits at `dy = +18.053` and the
denominator at `dy = -18.293`. Only `MathPainter.Draw` emits the `Scale(1,-1)`.
Najm's portable canvas must apply the flip explicitly or bake it into the
`VectorPicture` transform, and it must compose correctly with the upright rule
(NAJM-TEXT I.9), which is a separate flip.

**Where the upright flip actually lives** (added once the text slice landed, so
this entry does not send its reader to the wrong place): not at the layer, but at
the node. `TextNode` composes `Translate(-anchorOrigin) * Scale(1, flip)`, taking
`flip` from `Layer.YAxisPointsUp` at attach — a layer *fact*, applied by the node.
Fast math's flip therefore composes against a node-level transform, and a math
node placing a `VectorPicture` must reach the same reading frame `TextNode` does
rather than inventing a second convention. Two flips of the same sign silently
double; two of opposite sign silently cancel. Both render.

**Status:** Open — recorded before Fast math is built, so neither is
rediscovered by debugging wrong output.

---

## 9. `IImage` admits externally owned, draw-stable images

**Docs:** §4.4 describes `IImage` as an immutable, explicitly owned image snapshot, and §5.3
adds that snapshots are valid only inside the current render call. Separately, §7.5 documents
the external GPU interop pattern — wrapping an author's own GL texture as an `IImage` so a
custom pipeline is "an ordinary drawable that owns its render-to-texture privately", realized
per NAJM-SKIA I.7 through `GRBackendTexture` + `SKImage.FromTexture`.

Those two statements conflict for the case the interop pattern exists to serve. An author
re-rendering into their own texture every frame does not hold it immutable.

**Decision:** `IImage` admits a second, explicitly documented kind: externally owned, where
the caller guarantees stability **for the duration of a draw** rather than forever. Engine-
produced images keep the immutable contract unchanged.

**Why:** requiring true immutability would force a fresh texture per frame. `SKImage.FromTexture`
only borrows — verified by re-wrapping the same texture id after disposing its image — so
nothing would ever delete the discarded ones, and the documented pattern would leak a texture
per frame by construction. Draw-stability is the weakest guarantee that keeps composition
correct, and it leaves every other lifecycle rule intact: the image is still borrowed, still
invalid outside the render call, still never stashed.

Consequence for the realization: wrap once and cache it, invalidating only when the texture is
reallocated. Re-wrapping per frame would allocate per frame and break the zero-allocation
budget for no benefit, since a stable-size texture keeps its id.

**Status:** Implemented, alongside the GPU surface provider.

Three things measured during implementation refine the entry. The release callback fires
when Skia's last reference to the texture drops — for an undrawn or already-flushed image
that is at dispose, not at a later flush — so the safe ordering is dispose, flush, then
delete. `GRBackendTexture` may be disposed immediately after the wrap, because Skia copies
it. And `SKImage.FromAdoptedTexture`, the transfer-ownership alternative, failed to sample
correctly even on first use and is avoided.

One correction to an assumption made elsewhere: drawing a GPU-backed wrapped image on a CPU
raster context does **not** throw and does **not** draw nothing. Skia reads the texture back
and draws it correctly. `RenderCaps.GpuBacked` therefore guards a silent per-draw download,
not a correctness failure — a weaker guarantee than "fails fast on a non-GPU target" implies,
and worth knowing before relying on the cap to catch a misconfiguration.

A caution for the hydrogen integration: reallocating a texture under a **new** id leaves the
old wrap cached against a stale image. `ReleaseGlTexture(id)` exists for that, and any resize
path that regenerates names rather than reusing them must call it.

---

## 10. `Scene.Render` currently has two semantics

**Docs:** §4.1 gives `Scene.Render(IRenderTarget)` one meaning — delegate to the compositor
acquired at `Load`. `Load` takes a `SceneEnvironment`, which supplies the provider.

**Current state:** `SceneEnvironment` does not exist yet, so `Load` gained a transitional
`internal void Load(ISurfaceProvider?)` and `Render` falls back to a single-context traverser
walk when no provider was supplied. The two paths do **not** agree, and the disagreement is
observable:

- **Composited path (correct):** every participating layer's `ClearColor` is content. A layer
  that draws nothing but clears opaque blue still merges opaque blue over everything beneath
  it, exactly as NAJM-COMPOSITOR requires — "a layer whose root subtree culls to nothing still
  binds and clears (its `ClearColor` is content)".
- **Fallback path:** the frame background is the *bottom* participating layer's `ClearColor`
  and upper layers' clear colors are ignored entirely.

`SceneRenderPipelineTests.ALayerThatCannotContributeIsNotCleared_NotWalked_AndDoesNotRunItsHooks`
asserts the fallback reading (green where the compositor correctly produces blue). It is
green-lit only because it exercises the fallback. The composited reading is separately covered
in `LayerCompositionTests`.

**Decision:** keep the fallback until `SceneEnvironment` lands, then delete it — `Load` will
always carry a provider, `Render` will always composite, and that test's expectation changes to
blue. `RenderDirect` remains the only other path, and it needs its own answer for `ClearColor`,
which the reference never states for the direct path: paint each participating layer's clear as
a viewport-covering fill before its tree, which reproduces composited semantics for the
`SrcOver` content M1 allows.

**Why not converge now:** the fallback is the only reason ~20 existing tests can call
`Load()` without a provider. Converging is a mechanical change to how those tests load a scene
plus one expectation flip, and it belongs with the `SceneEnvironment` slice that removes the
need for it, not bolted onto the compositor slice.

**Status:** Superseded. `SceneEnvironment` landed, `Load` always carries a provider,
the fallback is deleted, and `Render` always composites. The expectation flipped to
blue as scheduled.

One thing this entry got wrong while it was open, caught by an independent audit:
it framed the divergence as being about `ClearColor` alone. It was not. See entry
11 — the direct path also drops layer `Opacity`, `Blend`, and `Viewport`. Recording
only the disagreement I had happened to notice left the larger one unregistered.

---

## 11. `RenderDirect` drops layer presentation

**Docs:** §5.3 and NAJM-COMPOSITOR I §3 require the direct path to clip each layer
to its viewport rect, apply `Opacity` via `PushOpacity`, apply layer `Blend` via a
context bracket where expressible, and open a per-layer isolation bracket when the
subtree contains a non-default blend or a backdrop.

**Current state:** `RenderTraverser.RenderLayers` does none of these. The only
presentation it honours is the `Opacity == 0` skip. A layer at `Opacity = 0.5`
renders fully opaque on the direct path and half-opaque through the compositor.

**Why this matters more than it looks:** the shared traverser exists precisely so
the two paths cannot drift, and they have drifted anyway — inside the type meant to
prevent it. `VectorExporter` is a direct-path client by construction, so this will
land as silently wrong SVG and PDF the moment the writers arrive, with no test
failing.

**Decision:** the direct path must apply viewport clip, opacity, and blend per
layer. `ClearColor` needs an answer the reference never gives for this path: paint
each participating layer's clear as a viewport-covering fill before its tree, which
reproduces composited semantics for the `SrcOver` content M1 allows. Isolation
brackets stay M2.

**Status:** Implemented. The direct path now opens a per-layer bracket carrying clear,
opacity, blend, and an optional device-space viewport, and every direct-path frame is
asserted byte-identical to the composited frame of the same scene — the property the
shared traverser exists to guarantee, which no individual property test would have
caught.

Closing it required an M1 ancestor of the composition SPI. `SetEngineTransform` rejects
a non-empty author stack and the traverser calls it per node, so the walk could not be
wrapped in `PushOpacity`. `BeginLayerBracket`/`EndLayerBracket` are engine-owned and
tracked at a separate depth: an open bracket is tolerated, outstanding author pushes are
still refused, and `EndPass` names the two kinds separately. `BeginUnit`/`BeginMask`,
with effects and masks, remain M2.

Two decisions inside it are worth keeping visible. The clear colour rides on the bracket
rather than arriving through a new rect-fill primitive, which would have meant either
widening the drawing surface or building a `PathBuilder` per layer per frame. And the
viewport is in **device** pixels, because the engine transform inside the bracket varies
per node, so a viewport in any local space would have no fixed meaning; it rounds its
origin and ceils its extent by the same rule the compositor uses, so a fractional
viewport covers identical pixels on both paths.

FP-5, the reference's direct-path bracket skip, is deliberately **not** implemented. It
needs the subtree predicate that is M2, and skipping unconditionally would let a
non-default node blend composite against the frame on the direct path while compositing
against its own layer on the composited one. A test pins that, and the naive skip fails
exactly it.

---

## 12. The ffmpeg frame sink was built ahead of its milestone

**Docs:** `ROADMAP.md` places "FFmpeg sink and live capture" in M2. `PLAN.md`
"Explicit deferrals" lists "Live capture, FFmpeg, and audio" as deferred until a
named production pulls them forward, and the promotion rule requires an acceptance
production that cannot be built without it.

**What happened:** `FfmpegFrameSink` and its options and tests were built during
the M1 delivery slice. Two real reasons drove it. The author's stated working
method is to script animations with coroutines and video-export them, iterating
headless; and this machine had ~3 GB free at the time, where a twelve-second 4K60
PNG sequence is roughly 30 GB, so a sequence-first design would have failed on
first use.

**Assessment:** the constraint was real, but the promotion rule was not formally
satisfied — no acceptance production existed yet, and `PLAN.md` resolution 8 had
already designated the PNG still as the working loop for seeing changes. The
substantive failure is process rather than code: this register's premise is that
entries are added when the decision is made, and the largest scope departure in the
tree went unrecorded until an audit found it.

**Status:** Recorded retroactively, which is itself the defect. The code stands; the
discipline slipped.

---

## 13. `TypesetRequest.Text` is a `string`, not `RichContent`

**Docs:** `NAJM-TEXT.md` I.3 types a typeset request's content as `RichContent` —
a span model carrying per-range style overrides, inline pictures, and rules — and
describes plain text as "a degenerate `RichContent`".

**Decision:** the baseline slice types it `string`. `RichContent` is not defined,
not stubbed, and not referenced.

**Why:** the degenerate case is carried in the type it actually is. The
alternative was a one-span `RichContent` shell standing in for a model whose
interesting members — span splitting, style resolution across a run boundary,
inline object metrics — this slice does not implement, which would have made the
request type look finished while every path through it handled exactly one span.
When the span model lands, `Text` gains a `RichContent` overload and the string
form stays as the convenience it already is, so nothing written against this
signature changes meaning.

**Consequence:** `ITextLayout` likewise declares only what plain text produces.
`Rules`, `Pictures`, `Fragments`, the caret pair, and `BakePath` are absent
rather than present-and-empty, so reaching for one is a compile error naming the
missing capability rather than a silent empty result.

**Status:** Implemented, and deliberately temporary. This is the entry to revisit
first when Fast math or rich styling starts, because both need the span model
before anything else.

---

## 14. `TextNode.MaxWidth` exists in order to refuse

**Docs:** `NAJM-TEXT.md` I.10 lists `MaxWidth` among a text node's properties, as
the wrapping width. Automatic line breaking is the UAX #14 stage (II.7), which
this slice does not implement.

**Decision:** the property is declared. Its getter returns null and its setter
throws `NotSupportedException` for any non-null value.

**Why:** the three options were to omit it, to accept and ignore it, or to refuse
it. Ignoring is the one that is definitely wrong — text that does not wrap is
indistinguishable from text whose width was wrong, so the bug presents as a
layout mystery with no error anywhere near its cause. Omitting is quieter but
sends an author following the reference into a compile error that names nothing.
Refusing fails at the property set, in the author's own code, with a message
saying to use `\n` — VI.3's fail-loud rule applied at the earliest moment it can
be applied.

**Why it is safe to reverse:** when wrapping lands this becomes an ordinary
property, and nothing that compiles today changes meaning — code that compiles
now either never sets it or sets it null.

**Status:** Implemented, and a placeholder by construction. Unlike most entries
here, this one is designed to be deleted.

---

## 15. Frame diagnostics: nothing in the reference describes a frame numerically

**Docs:** §16 gives `Najm.Core` the delivery contracts (`IFrameSink`,
`PixelFrameLease`, `OfflineRenderer`) and `Najm.Skia` the media backend that
realizes them. The only counter type anywhere in the reference set is
`CompositorStats` (NAJM-COMPOSITOR §9), and it counts *composition constructs* —
brackets opened, nesting depth, RB barriers, layer-target bytes, pool events. It
says nothing about pixels. Meanwhile the reference leans on pixel-level answers
constantly: NAJM-COMPOSITOR §10 requires eleven goldens phrased as
"byte-identical", the determinism posture (§2.2) is stated as "two replays hash
identically", and NAJM-TEXT §V asks for byte-identical geometry across runs.
Every one of those is a question about a rendered frame, and no seam in the
reference can answer any of them. There is no `FrameStats`, no frame diff, and no
way to read a written frame back at all.

**Decision:** add a frame-diagnostics family beside the delivery seam.

In `Najm.Core/Delivery`, portable and backend-neutral because they are arithmetic
over a decoded buffer:

- `LevelHistogram` — the exact 256-bucket distribution of one 8-bit channel, with
  nearest-rank percentiles, counts at or above a level, min, max, and mean.
- `FrameStats` — one pass over a `PixelFrameLease` or a raw span: per-channel and
  luma histograms, white/black clipped-pixel counts against a caller-chosen level,
  and a linear-light luminance summary with the frame's dynamic range in stops.
- `FrameComparison` / `FrameDifference` — byte identity first, and when frames are
  not identical, how many pixels differ, by how much at worst, on average, where
  the first difference is, and the bounding box of all of them.

In `Najm.Skia/Delivery`, because decoding a file is exactly the "image decoding"
row §16 already assigns to the backend:

- `FrameProbe` — reads a written image back into a `PixelFrameLease`, and the
  three conveniences that follow from it (`Measure`, `Compare`, `AreIdentical`).

**Why:** twice now, work built on this engine has had to construct its own
measurement apparatus because the engine cannot describe its own output. Grading
a volume render is a loop of render, measure, look, and the measure step was
served by a PNG decoder written from scratch in Python, on a machine with no
numpy, no PIL and no ImageMagick. A second study transcribed an entire volume
integrator onto the CPU partly to reach pixel statistics. Both wanted the same
small set of numbers — the fraction of pixels clipped to white, a p90, a
per-channel mean, and a diff against a reference frame.

The split follows the dependency rule rather than convenience. Statistics over a
decoded buffer need nothing but arithmetic, so they are portable and testable
without a backend; obtaining the buffer from a PNG needs a decoder, so that one
call sits in `Najm.Skia`. `Najm.Core` gains no dependency, and a future backend
inherits the whole family by implementing `IImage.CopyPixels`, which it must
implement anyway.

**Colour space, stated because getting it silently wrong would poison
everything built on it.** Two luminances are reported and they are not
interchangeable. `FrameStats.Luma` is Rec. 709 luma over the *encoded* sRGB
bytes, rounded to a level — a display-referred number in the same units as the
pixels, which is what "the pixel is at 254" and "p90 is 181" mean, and what the
clipped-pixel counts are consistent with. `MeanRelativeLuminance` and its
companions linearize each channel through the sRGB transfer function first and
then apply the same coefficients, giving CIE relative luminance in linear light —
the only one of the two in which a ratio is a physical ratio, hence the only one
`DynamicRangeStops` may be derived from. Percentiles exist on the encoded
histogram and deliberately not on the linear summary: percentiles commute with
monotone transforms, and linear luminance is not a monotone function of encoded
luma, so a linearized p90 would be a different pixel's value rather than the same
pixel's value in other units.

**Cost:** none, by construction, and now measured rather than asserted. Nothing in
the family is reachable from the render path; it is called by whoever wants a
number, after the frame exists. `FrameComparison.AreIdentical` and
`FrameComparison.Between` allocate **zero** managed bytes — `Between` measured on
both its paths, since the identical path skips each row early and would hide a
per-pixel allocation on the path that does the work. `FrameStats.Measure` into a
reused instance allocates **zero** per frame, and a fresh `FrameStats.Of` allocates
the same amount for a 512x512 frame as for a 64x64 one: its histograms, not its
pixels. All four are pinned by `AllocationProbe`, whose settle-and-retry protocol
is what makes the readings survive method-level test parallelism.

**Status:** **Implemented and tested.** 48 tests across
`LevelHistogramTests`, `FrameStatsTests`, `FrameComparisonTests`,
`FrameDiagnosticsAllocationTests` (Core) and `FrameProbeTests` (Skia); the suite is
at 718.

What they pin, and why each was chosen so that a plausible wrong implementation
fails it:

- **Percentile boundaries, in both percentile-bearing types.** `Percentile(0)` is
  exactly the minimum and `Percentile(100)` (quantile `1.0` on `FrameStats`) is
  exactly the maximum. Eight samples at eight distinct levels queried at p30 is a
  three-way discriminator: nearest rank answers 30, a `floor` rank answers 20, a
  strict `>` on the cumulative comparison answers 40. Verified by injecting each
  wrong definition in turn — the `floor` rank fails 2 tests and the strict `>`
  fails 7.
- **Cumulative counts at both ends**, including `CountAtOrAbove(255)` and
  `CountAtOrBelow(0)`, plus a loop asserting that the two halves partition the
  sample at every one of the 255 split points. The 255 case is the direct
  regression test for the `byte` loop index this family shipped with.
- **Clipping counts as a joint question.** A frame containing saturated pure reds
  must report them in red's top bucket and *not* in the clipped-white population,
  which is what `ChannelFloor`/`ChannelCeiling` exist to make possible; the
  threshold is exercised at 255 and at 254.
- **The two brightnesses are different numbers.** Pure green measures luma 182 and
  relative luminance 0.7152; mid grey measures luma 128 and relative luminance
  0.216. The colour-space paragraph above is now a test rather than a claim.
- **Difference reporting**: bounding box spanning three scattered pixels, the first
  difference in reading order (deliberately not the leftmost), the worst single
  channel move, symmetry under swapping the operands, and the empty box reading as
  zeroes for an identical pair.
- **Identity versus refusal on mismatched inputs**: differing size and differing
  format both answer false from `AreIdentical` and both throw from `Between`.
- **Stride padding is never measured and never compared**, on both sides.
- **The round trip through the backend**: a frame written by `SkiaPngWriter` and
  read back by `FrameProbe` is byte-identical, which is the property the decoder's
  "no colour space on the destination info" decision exists to hold.

Writing them found three defects beyond the `byte` loop index, all now fixed:

1. `FrameStats.Percentile` computed its rank in `double`. `0.07 * 100` is
   7.000000000000001, so the 7th percentile of a hundred-pixel frame silently
   returned the 8th pixel. `LevelHistogram` already computed its rank in `decimal`
   and documented why; `FrameStats` did not. It does now.
2. `FrameComparison.Between` returned `(0, 0, -1, -1)` as the empty bounding box
   while its own comment said "zeroes" and `FrameDifference`'s documentation said
   all four were `-1`. Three descriptions, no two alike. It is four zeroes now,
   documented as such; `FirstDifferenceX`/`Y` stay `-1`, because `(0, 0)` is a real
   position and zero there would name a pixel.
3. `DynamicRangeStops` documented the 8-bit sRGB ceiling as "about 12.4 stops". It
   is 11.69 — level 1 decodes to 0.000304 of full white. The number is now correct
   and pinned by a test.

One wrinkle this entry should not hide: `LevelHistogram` is public, fully tested,
and consumed by nothing. It survived the reconciliation of two overlapping drafts
in which `FrameStats` won, and it duplicates `FrameStats`'s histogram queries with
an incompatible convention — percentages 0-100 against quantiles 0-1. It is tested
rather than deleted because deleting public surface is a decision to take on
purpose rather than in passing, but the two conventions living one namespace apart
is exactly the sort of thing that later produces a measurement off by a factor of
a hundred. Either give `FrameStats` a `LevelHistogram`-returning accessor or remove
the type.

---

## 16. `ISurfaceProvider` states its own `RenderCaps`

**Docs:** NAJM-SKIA I.7 specifies the interop check as attach-time: "a drawable
holding a wrapped image validates `Caps.GpuBacked` (fail-fast on raster/offline
configurations)". §6.6 puts capability access at attach, through `Scene.Env`.
V.1's environment matrix gives every configuration a `Caps` column and §57's
mapping row fixes the values: `GpuSkiaSurfaceProvider ⇒ SkiaSurface | GpuBacked`,
`RasterSkiaSurfaceProvider ⇒ SkiaSurface`.

What the reference never says is *where an attached node reads that from*.
`ISurfaceProvider` declared `CreateTarget` and `CreateCompositor` and nothing
else, so the mapping existed only on the two concrete Skia classes.
`SceneEnvironment.Caps` existed but was a value a host passed in, and
`OfflineRenderer` passed none — leaving `RenderCaps.None` on a GPU offline run,
the one configuration where a GL-texture drawable is correct.

**Decision:** add `RenderCaps Caps { get; }` to `ISurfaceProvider`, with no
default implementation, and have `OfflineRenderer.Render` and
`OfflineRenderer.RenderStill` copy `surfaces.Caps` into the `SceneEnvironment`
they build.

**Why:** the check I.7 describes normatively could not be written. Attach sees an
environment, the environment's provider is an `ISurfaceProvider`, and the
interface had no capability member — so the only honest place left to ask was
`context.Caps` inside `Render`, which is a frame after the decision and therefore
a diagnostic rather than a contract. Three projects in a row wrote that check in
the wrong place because it was the only place. The member is the two lines that
make the specified behaviour expressible.

No default implementation, deliberately. `RenderCaps.None` would have kept every
existing implementer compiling, and it is also exactly the wrong answer for a
backend that forgot to say: content declines to attach, correctly, with no
indication that the provider — not the content — is what is misconfigured. The
four in-tree test doubles were four one-line edits. This is also why the interface
carries the pre-release notice it does.

**Why the environment's `Caps` is still a separate value.** The obvious next step
would be for `SceneEnvironment` to derive `Caps` from `Surfaces` and stop taking
the parameter. It does not, because V.1's vector-export row is a live
counterexample: that configuration holds a *raster* provider as staging while the
writer context carries `SkiaSurface | VectorTarget`, so the environment's
capabilities and its provider's are not always the same fact. The driver that
assembles an environment is responsible for keeping the two in step, and the
offline driver now does. A host is the next one that will have to, and nothing
enforces it — that is the remaining gap, recorded rather than papered over.

**What this does not do: remove the downcast.** A GL-interop scene still opens
with `Env.Surfaces as GpuSkiaSurfaceProvider`, because `WrapGlTexture`,
`ResetGlState`, and `Flush` are backend-specific by definition and do not belong
on a portable interface. What changes is what the cast is *for*: it was carrying
two jobs, validation and access, and validation was the one it did badly — a cast
that fails says the provider is the wrong type, not that the target cannot do GL,
and the two stop coinciding the moment a second GPU backend exists. The
capability check is now `Env.Surfaces.Caps.HasFlag(RenderCaps.GpuBacked)` and the
cast is left doing only what it is genuinely for. No helper was added to hide it:
a one-line convenience over a cast is a second name for a language feature, and
the reporter of this friction said plainly they had no better idea than the cast.
The gap that could be closed was the documentation, and
`GpuSkiaSurfaceProvider`'s class remarks now carry the two-step idiom verbatim.

**Status:** Implemented.

---

## 17. The offline entry points take a backend

**Docs:** NAJM-SKIA V.1's environment matrix gives "Offline (`SkiaOffline.Render`)"
exactly one row, and that row's `Surfaces` cell is `RasterSkiaSurfaceProvider`
with `Caps = SkiaSurface`. V.2 sketches `SkiaOffline.Render(Func<Scene>,
OfflineOptions)` with no backend parameter. The GPU provider appears only in the
`DesktopHost` row, whose GL context comes from the host (§4.6).

So the reference has no offline GPU configuration at all. It is not that it
forbids one; it never contemplated one, because it assumed the only reason to
want a GPU was a window.

**Decision:** add `OfflineBackend { Raster, Gpu }` and an optional `backend`
parameter to `SkiaOffline.Render` and `SkiaExport.Png`, defaulting to `Raster`.
`OfflineBackend.Gpu` builds `HeadlessGlContext.Create()` and
`GpuSkiaSurfaceProvider.CreateOver(glContext, ownsGlContext: true)`. Also add
`sampleCount` to `SkiaExport.Png`, which `SkiaOffline.Render` already had through
`OfflineOptions`.

**Why:** the interop seam (I.7, deviation 9) exists so an author can render their
own GL into a texture and let the engine composite it. Every scene that uses it
needs `RenderCaps.GpuBacked`, and the documented export route could not produce
that target, so *every* such project has written the same ten lines: create the
context, create the provider over it with `ownsGlContext: true`, construct the
scene, call `OfflineRenderer` directly, dispose in the right order. Three
independent copies now exist — the fractal sample, the Najm GPU tests, and an
external presentation project — which is the number at which duplication stops
being a coincidence. The dispose ordering in particular (`GRContext` released
while its GL context is still alive) is a convenience's job, not an author's: get
it wrong and the failure is a crash at shutdown, after the frames are already
written and the run looks successful.

`sampleCount` goes on `Png` because the GPU backend is what makes it mean
anything. Raster Skia is analytically antialiased and normalizes every count to
one, so the parameter was genuinely useless before; a GPU surface multisamples,
and a GPU still at one sample has hard edges that look like an engine defect. The
route that exists so an author can *look* at a frame should not be the route that
cannot ask for the frame to look right.

**What was deliberately not added.** No public factory for the provider itself.
The ten duplicated lines are the *driver*, and that is what the parameter
absorbs; an author who wants the provider on its own — to read `MaxTextureSize`,
or to print the GL banner — already has two public calls that do it, with the
ownership flag that makes the teardown correct. A third way to build the same
object would be surface without a friction behind it.

**The honest cost.** `OfflineBackend.Gpu` is Linux-only, because
`HeadlessGlContext` is, and it fails from the entry point with
`PlatformNotSupportedException` rather than at a loader boundary. And it narrows
determinism: two GPU runs on one machine agree, two machines with different
drivers need not, so §2.2's "two replays hash identically" remains a statement
about the raster row and nothing here promises to extend it. Both are documented
on the enum members rather than left for a reader to discover.

**Status:** Implemented.

---

## 18. `FrameSink.PngFile`

**Docs:** V.3 and V.4 specify `FrameSink.PngSequence` and `FrameSink.FfmpegPipe`.
Neither the reference nor deviation 4, which added `SkiaExport.Png`, mentions a
sink that writes one named file; the still export's sink was written as an
internal implementation detail of that convenience.

**Decision:** make `PngFileFrameSink` public — public type, internal constructor,
reached through a new `FrameSink.PngFile(string path)` factory, exactly as
`PngSequenceFrameSink` and `FfmpegFrameSink` already are. Add a `Path` property.

**Why:** `SkiaExport.Png` only serves a caller happy with everything else it
decides. A caller driving `OfflineRenderer.RenderStill` directly — because they
need an explicit output size, a pixel format, or a provider they already own —
had no public route to a named PNG at all. What they wrote instead, three times
now, is a `PngSequence` into a scratch directory, a `File.Move` of
`still_00000.png`, and a `try/finally` that deletes the directory: eleven lines
whose only purpose is to rename a file, and which quietly depend on the sequence
sink's zero-padding format.

The sink is not a special case of the sequence sink and should not be emulated by
one. It takes a path rather than a directory and a stem; it refuses a stream
declaring more than one frame instead of overwriting the same file per frame; and
it refuses a stream that submitted nothing instead of reporting success over a
file that does not exist. All three behaviours already existed and were only
unreachable.

**Status:** Implemented.

---

## 19. `Wait.Until` evaluates its predicate in the pass that yields it

**Docs:** §10.3's wait table gives `Wait.Until(pred)` as "resume on the first pass
where `pred` is true", and the per-wait note adds that `pred` "is evaluated **once
per eligible pass**, during the pass — treat it as a pure read of scene state".
§10.4's `Step` table lists `Until` among the waits a step deems satisfied with
`pred` not called. What none of them settles is whether the pass that *yields* the
wait is one of the passes the predicate is evaluated in.

**Decision:** it is. The predicate is evaluated at the moment the wait is yielded,
still inside that pass, and once per eligible pass after that; a predicate that
already holds resumes the routine in the same pass, with no suspension. Everything
else follows the reference: not evaluated for a paused routine or one under a
disabled owner, not evaluated by `Step`, and — Najm's own choice, since the
reference does not say — a throw from the predicate faults the routine exactly as
a throw from its body does, disposing the enumerator so the author's `finally`
blocks run.

**Why:** `Until` exists to replace `while (!c) yield return Wait.NextFrame;`, and a
spin tests its condition **before** it suspends. Under the other reading —
suspend, then ask at the next pass — a condition that already holds costs the spin
nothing and costs `Until` one frame, so replacing one with the other would retime
the routine by a frame in exactly the cases where the condition is cheap to
satisfy. That is the defect this member was built to remove, and an implementation
that reintroduced it one call site over would be worse than not having the member:
the trap would now be inside the engine's own convenience rather than in the
author's spelling. The eager reading is also the more literal one — "the first
pass where `pred` is true" includes the pass in which the wait was created.

The cost of the choice is one shape of author bug that the spin cannot produce: a
loop whose body yields nothing but already-true `Until` waits never returns control
to the pass. It is the same infinite loop as a `while (true)` body with no `yield`
in it, reached a less obvious way, and it is documented on the member.

`Step` is the one place the two readings are not reconciled, and deliberately: a
step that landed on an already-true `Until` and ran on through it would resume the
routine twice, breaking `Step`'s documented "resumes exactly once". A stepped
routine therefore leaves such a wait to the next pass.

**The other half of the same finding.** §10.2's FIFO rule says nesting "compose[s]
without a one-frame hiccup". That is true of the **entry** — a child started by
`Wait.For(routine)` is appended to the queue and gets its first resume later in the
same pass — and false of the **exit**: the pass drains once per tick in enqueue
order and a parent sits ahead of any child it started, so the child's completion is
not observed until the next pass. One pass per level of nesting, at the rejoin.

Nothing in the implementation changes here; the behaviour is what the FIFO rule
already implies and it is not obviously wrong. What was wrong is that only the
favourable half was written down, on a member whose whole purpose is to be hidden
behind a helper's name — so an author who factors a spin into a helper silently
moves the thing it was waiting for by a frame. It is now stated on
`Wait.For(IEnumerator<Wait>)` and on `Wait.For(CoroutineHandle)`, next to the entry
claim, with `Wait.Until` named as the way to avoid paying it. `Wait.For(AnimationHandle)`
genuinely does not pay it, because the tween pass completes before the coroutine
pass begins, and says so.

**Status:** Implemented.

---

## 20. `Animate` has a `double` overload

**Docs:** §10.6's one example is
`Animate(v => circle.Radius = v, from: 0, to: 40, duration: 0.6, ease: Ease.OutCubic)`,
which never states the width of `v`, `from` or `to`; the property it drives is a
float, like every coordinate in the drawing model, and Najm implemented the member
float-only. The reference does not contemplate a second width, nor forbid one.

**Decision:** add `Action<double>` overloads of `Scene.Animate` and `Node.Animate`,
both easing flavours, beside the float ones. Internally `Animation` becomes an
abstract base holding the timing — elapsed, the reached-the-end rule, status,
eligibility — with `FloatAnimation` and `DoubleAnimation` holding the endpoints and
the setter. The float path's arithmetic is unchanged, expression for expression.

**Why:** the *drawing* model is float and should stay float, but the quantities a
scene animates are frequently not. The scene that reported this drives a camera
azimuth in degrees, a grade parameter, and a radius in Bohr radii, all doubles
because that is what the physics they come out of holds them in. Every tween of one
was a lambda that widens, a `with` expression to write back through, and `f`
suffixes on numbers that are not floats — three of them in an eleven-line routine.
The endpoints were rounded to float before the tween ran, so a ramp to a stated
`155` landed on the double nearest the float nearest 155.

**What is not double.** The easing curve. `ITimingFunction.Evaluate` is a float
contract in `Najm.Utils` and widening it would touch every curve, every property
helper, and the reference's own `Ease` surface for a resolution nobody has asked
for. So a double tween interpolates in double, between exact double endpoints, with
a single-precision curve: about seven digits of the interval, anchored at endpoints
that are exact, and a final write that is the to-value itself. This is stated on the
member rather than left to be discovered.

**The sharp edge, and why it is documented rather than designed away.** Which
overload a call site reaches is decided by the endpoint arguments, and an int
literal converts to float in preference to double — so `Animate(v => azimuth = v,
0, 90, 1d)` over a `double azimuth` compiles, silently runs the *float* tween, and
differs only in the last digits. Nothing can be done about that from inside the
language's rules; both members therefore say so, and a test pins it by reading the
boxed type of the setter's parameter. It is also why the reference's own example,
transcribed verbatim, still reaches the float overload.

**What was deliberately not added.** No generic `Animate<T> where T : INumber<T>`.
It would subsume both overloads and infer cleanly from the endpoints, but it admits
`int` and `decimal` — an int tween would round every intermediate value and look
like a stutter rather than an error — and it turns one small pair of members into a
generic-math surface with an instantiation per element type. Two overloads state
exactly what the engine will tween, and there are exactly two widths a scene has.

**Status:** Implemented.

---

## 21. An offline run with no stated length runs until the scene's work finishes

**Docs:** §18 and NAJM-SKIA V.2 give an offline run a length: a duration or a
frame count, converted to ticks by the fixed-step rule. Nothing in the reference
contemplates a run whose length is decided by the scene, and Najm's own
`ResolveFrameCount` treated the absence of both as a configuration error.

**Decision:** neither `Frames` nor `Duration` — `OfflineOptions.RunsUntilIdle` —
now means "tick until the scene has no unfinished routine or tween, and submit
that last frame". Three members carry it: `RunsUntilIdle`, a `MaxFrames` ceiling,
and `Scene.HasScheduledWork`, which is the question the loop asks and is public
because otherwise the rule the mode runs on is unobservable from outside the
engine. `ResolveFrameCount()` still throws for this configuration, with a message
that now says why rather than blaming the caller.

**Why:** the alternative is what the reporting scene actually does — publish its
own duration by hand, as the sum of its beat constants — and that sum is **wrong**,
in the direction that loses picture. Waits add whole frames the constants cannot
see: a spin on a condition, and one per `Wait.For` rejoin (deviation 19). The
scene that reported it drifts about ten frames past its stated duration, the
exporter cuts at the stated duration, and nothing anywhere says so. It survives
only because that clip's last beat is a half-second tail with nothing in it. The
scheduler knows when the routines are done; nothing else in the system does, and
no arithmetic an author can write recovers it.

**What counts as unfinished.** Every routine and every tween that has not reached
a terminal status — not only coroutines, though "until the coroutines finish" is
how the request was phrased. A scene whose last motion is a tween is not finished
while that tween is still writing values, and a run that stopped at the last
routine would cut it. Paused work, and work under a disabled node, is unfinished
too: it has stopped running, not stopped existing.

**The two costs, both deliberate.** The length is discovered rather than declared,
so the sink is begun with a null `FrameStreamInfo.FrameCount` — already a legal
value, meaning "a live capture that runs until the user stops it", and the one
sink that cares (`PngFile`) already accepts it. And a routine that never completes
would run forever, so `MaxFrames` bounds it, defaulting to one hour of simulated
time at the run's rate. **Reaching the ceiling throws.** Returning the frames
rendered so far would be a truncated clip reported as a finished one, which is
precisely the failure this mode exists to remove; a run that cannot answer the
question must say so rather than answer it wrongly.

**What this changes for existing callers.** One behaviour: a configuration with no
length used to throw before the scene was touched, and now runs. That was a
fail-loud on an omission, and it is being spent on a feature — the trade is
deliberate, and the ceiling is what keeps the omission from being unbounded. A
zero-length run is still expressible and still means zero: `Frames = 0` is a
stated length, not an absent one.

**Status:** Implemented.

---

## Documentation conflicts

Places where the reference set disagrees with itself. Recorded so the
resolution is visible and does not look like an implementation error.

### Fast-path numbering

`NAJM-COMPOSITOR.md` §3 defines FP-1…FP-5 with "FP-5 — direct-path layer bracket
skip". `NAJM-SKIA.md` III.2 calls the same predicate "FP-6's registry-counted
predicate". The compositor document owns the fast-path table, so its numbering
is authoritative. "Registry-counted predicate" appears only in the Skia document
and is never specified; `ARCHITECTURE.md` §5.3 phrases the same rule as "an
enumerable skip condition".

### Easing names

`ARCHITECTURE.md` §10.6 writes `Ease.OutCubic`; Appendix B.1 writes
`Ease.CubicOut`. Already resolved by `PLAN.md` resolution 3 in favour of
direction-first (`Ease.OutCubic`), which is what `Najm.Utils` implements.

### M1 scope: ROADMAP vs. PLAN phasing

`ROADMAP.md` M1 scope includes "Desktop live preview", "Method-body hot reload
plus manual `F5` warm restart", and the debug overlay. `PLAN.md` places the
desktop host, hot reload, and initial diagnostics in **Phase 4**, under
"Provisional scope: phases 4 through 6".

**Resolved** by `PLAN.md` resolution 8: this plan governs, and all four stay in
phase 4. Development runs on a headless VPS where a windowed host cannot run at
all, so the deferred items were untestable in the only available environment.
Rendered PNG therefore becomes the sole means of seeing a change, and acceptance
stays visual through rendered stills rather than a live window.

### `Najm.Text`'s CSharpMath dependency

`NAJM-TEXT.md` §0 pins `CSharpMath.SkiaSharp`, and `ARCHITECTURE.md` §16 lists
`Najm.Text`'s dependency row as "Core, SkiaSharp, HarfBuzzSharp, CSharpMath".
`PLAN.md` resolution 7 says the opposite: "Fast math uses `CSharpMath.Rendering`
through a Najm-owned portable canvas that records an atomic `VectorPicture`;
`Najm.Text` does not depend on `CSharpMath.SkiaSharp` or SkiaSharp."

`PLAN.md` is later-dated and claims to resolve gaps "unless later architecture
edits supersede them", and the implemented `Najm.Text.csproj` follows it —
HarfBuzzSharp and Najm.Core only, no SkiaSharp. The architecture test's
allowlist already anticipates `CSharpMath`/`CSharpMath.Rendering`.

**Resolved in favour of `PLAN.md` resolution 7**, by executing the Phase 1
compatibility spike that had never been run. `NAJM-TEXT.md` §0 and
`ARCHITECTURE.md` §16 are wrong on this point and are superseded.

Evidence, from a throwaway console app referencing only `CSharpMath` and
`CSharpMath.Rendering` at the pinned 1.0.0-pre.1:

- The backend-agnostic seam exists: `CSharpMath.Rendering.FrontEnd.ICanvas`
  (lines, rects, save/restore, translate/scale, colour and paint style) and the
  abstract `FrontEnd.Path` (`MoveTo`, `LineTo`, `Curve3`, `Curve4`,
  `CloseContour`). Its entire vocabulary is `System.Drawing.Color`/`PointF`/
  `RectangleF`. There is no image or raster command in `ICanvas` at all, so
  resolution 7's "atomic `VectorPicture`" is not merely possible — it is the
  only thing the API can emit.
- No SkiaSharp anywhere. `CSharpMath.Rendering`'s assembly references are
  `CSharpMath`, `netstandard`, `System.Memory`, `System.Numerics.Vectors`. The
  spike's build output was three DLLs and no `runtimes/` directory. A runtime
  check confirmed no assembly with "Skia" in its name ever loaded.
  `CSharpMath.SkiaSharp` is an 11 KB wrapper Najm can write itself.
- Raw OTF bytes work. `Typography.OpenFont` is ILMerged into
  `CSharpMath.Rendering`, so `new OpenFontReader().Read(stream)` over Najm's
  bundled `latinmodern-math.otf` yields the face directly — 4802 glyphs, MATH
  table present. Glyph-id identity (CS-R1) holds exactly: `a`/`x`/`√` resolve to
  66/89/3077 through both CSharpMath and Najm's own lookup.
- Real LaTeX round-trips to vector commands: `\frac{a}{b}` records 93 commands
  including the fraction rule as a `DrawLine`; `|\psi_{2,1,0}|^{2}` records 229.

Two API details II.6's prose gets wrong: `Result<T>` has no `.Value` — deconstruct
it (`var (list, error) = LaTeXParser.MathListFromLaTeX(s);`). And the parser is
lenient, so "fail loud with CSharpMath's message" is not always available:
`\frac{a}` succeeds as `\frac{a}{}` with a null error. Najm needs its own
validation pass if silent LaTeX repair is unacceptable.

One determinism caveat: `CSharpMath.Rendering` embeds three reference fonts in a
**static** registry, and `Fonts(local, size)` can only prepend overrides, never
remove the globals. A glyph missing from Najm's face silently falls back to
CSharpMath's embedded copy. "Pinned font bytes" in the §2.2 reproducibility
posture therefore means Najm's bytes *plus* the package's.

### Direct-path bracket predicate

The direct-path layer bracket triggers on "non-default blend or a backdrop"
(§5.3), while the node-tier isolation predicate also includes mask, effect,
opacity below one, and `Isolate` (§6.7). The docs do not say what the direct
path does with a layer whose subtree carries only effects. Unresolved.
