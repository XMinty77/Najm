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

**Status:** Open.

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

**Status:** Open.

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

**Status:** Open.

---

## 7. `Camera2D.FitRect` takes the virtual resolution

**Docs:** Appendix B.2 writes `layer.Camera.FitRect(Bars.GeometryBounds)` — one
argument. Fitting a rect requires knowing the viewport it must fit inside, and
that is `Scene.VirtualResolution`, which the camera does not own.

**Decision:** `FitRect(in Rect worldRect, in Vector2 virtualResolution)` for now.

**Why:** a one-argument form had no honest source for the viewport size when this
was decided; inventing a default would silently frame against the wrong box.

**Status:** Open, and the blocking reason has since gone away.
`Scene.VirtualResolution` now exists (`Scene.cs`), so the documented one-argument
convenience can be added on `WorldLayer2D`, which can reach its scene, forwarding
to the two-argument form. Author code should read as the reference shows. Note
the extent a viewport'd layer frames is its viewport, not the scene's virtual
resolution, so the convenience must forward the layer's framed extent rather than
`Scene.VirtualResolution` unconditionally.

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
(NAJM-TEXT I.9), which is a separate flip at the layer.

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
