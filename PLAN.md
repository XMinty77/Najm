# Najm Construction Plan

**Status:** Living implementation plan  
**Last updated:** 2026-10-05  
**Source of truth:** `docsref/BRIEF.md`, `docsref/ARCHITECTURE.md`,
`docsref/NAJM-COMPOSITOR.md`, `docsref/NAJM-SKIA.md`,
`docsref/NAJM-TEXT.md`, and `docsref/ROADMAP.md`.

This plan turns the architecture into vertical implementation slices. The
architecture documents own semantics; this document owns execution order,
acceptance gates, delegation, and current scope.

**Where facts live.** This document summarizes and sequences; it does not restate.
What has landed is `git log`. Why the code departs from the reference set is
`docsref/DEVIATIONS.md`, including every open question. Shortfalls that are
nobody's feature are `docsref/TECH-DEBT.md`. When this document and one of those
registers disagree, the register is right — see TECH-DEBT D-5 for why that rule
exists.

## Product priorities

Najm is a vector-first procedural graphics engine for educational animation,
interactive presentations, and publication figures. Its defining combination
is a game-engine-style scene graph, imperative immediate-mode drawing,
Illustrator-like composition, serious text and mathematics, deterministic
offline output, and live interaction.

The first productions that guide implementation are:

1. A sorting-algorithm animation with reusable, deterministic choreography.
2. An orbital identity clip and a publication-quality vector figure that prove
   raster and vector delivery from the same scene model.
3. An interactive presentation over an externally rendered GLSL ES scene,
   composited beneath crisp Najm-authored graphics.

2D is the priority. External GLSL ES renderers are integrated through the
external GL-texture seam rather than ported to SkSL, and that happens well
before Najm's native scientific 3D milestone.

**Schedule change, 2026-10-05.** The hydrogen-atom presentation that previously
sat first on this list and carried a September date has been cancelled. It
remains a real intended production and the sequencing consequence is only that
nothing jumps the queue on its behalf: the work it pulled forward — the GPU
surface provider, external-texture interop, the desktop host, input routing — has
all landed and is retained. The remaining near-term deliverable is the sorting
animation, which is `ROADMAP.md`'s M1 acceptance production #2, so completing M1
and shipping the deliverable are now the same programme.

## Delivery principles

- Build evidence-producing vertical slices, not broad collections of stubs.
- Establish permanent semantic seams early; defer unneeded implementations.
- Implement semantics before fast paths and require equivalence evidence for
  every optimization.
- Keep Core backend-neutral. Backend-native objects live in backend-owned side
  tables and never leak through portable handles.
- Fail loudly for unsupported author requests. Never silently change rendering
  or vector-export semantics.
- Treat deterministic replay, render idempotence, exact traversal order, and
  lifecycle exception safety as foundational contracts.
- Keep warm steady-state paths allocation-silent where the architecture marks
  them as hard zero-allocation targets.
- Run expensive builds and visual checks conservatively on the VPS. Do not run
  parallel 4K or native-heavy test jobs when memory pressure is elevated.

## Progress

- **M0 completed 2026-08-11:** exact .NET 10 SDK selection, centrally pinned
  dependencies, solution-wide locked restore, architecture guards over direct
  and resolved dependencies, native Skia and HarfBuzz checks, raw-pixel raster
  goldens, and a recorded allocation/surface-memory benchmark baseline in
  `benchmarks/M0-BASELINE.md`.
- **Phase 2 nearly complete.** The runtime, drawing, composition, motion and
  raster-delivery slices are all in: timing policies, the 2D tree and logical
  transforms, engine-controlled lifecycle, layers and behaviors, identity
  registration, FIFO deferred mutation, allocation-free Update traversal; the
  portable drawing surface through paths, strokes, gradients, affine images,
  clips, transforms, group opacity, tagged clear and the portable blend subset;
  the node render seam and the split per-node/subtree bounds; `Camera2D`,
  `WorldLayer2D` and layer presentation; the shared traverser, the ordinary-layer
  compositor and the direct path, with every direct-path frame asserted
  byte-identical to the composited frame of the same scene; node-tier `Opacity`,
  `Blend`, `Clip` and `Isolate`; the minimal scheduler, waits and tweens; and
  offline delivery — frame sinks, the deterministic fixed-step renderer, PNG
  stills and sequences, the ffmpeg pipe, and the frame-diagnostics family.

  **Remaining in phase 2, and it is the critical path to M1. Both were blocked on
  a decision; both were unblocked on 2026-10-06 and are ready to build:**

  1. A small effect path for glow and drop shadow. The direct-path bracket
     predicate for an effect-only subtree is resolved: the direct path and the
     node-tier predicate are **one predicate**, §6.7's list governs, and the
     existing direct-versus-composited byte-identity assertion extends to cover
     effect-bearing scenes (`DEVIATIONS.md`, documentation conflicts).
  2. SVG/PDF writers over the direct path, and the vector structural check.
     Deviation 11 prepared the ground — the direct path now carries per-layer
     clear, opacity, blend and viewport. `VectorPolicy` is now declared in
     deviation 38: a scene-level `{ Refuse, Raster }` enum defaulting to `Refuse`,
     where `Raster` still reports every unit it rasterized.

  The four audit defects this section previously tracked as unfixed — the direct
  path dropping layer presentation, unbounded descriptor caches, node-only
  `VisualBounds`, and top-left anchoring on mismatched aspect — are **all
  closed**. See `DEVIATIONS.md` entry 11 and commits `448fb7c` and `4d38f16`.
- **Phase 3 baseline text landed; math has not started.** Pinned Latin Modern
  Roman/Math assets with provenance and licenses, the internal HarfBuzz
  ownership/shaping path with explicit native lifetime, thread affinity, pool
  reset/reuse and RTL-cluster coverage, the public layout contracts, the layout
  cache, and `TextNode` in `Najm.Lib` with `DrawText` as a Tier-1 op are all in.
  Two deliberate absences are recorded as deviations: no automatic line wrapping
  (`MaxWidth` refuses rather than being ignored — entry 14) and no rich text, so
  mixed styling means several nodes sharing a baseline (entry 13).

  Remaining in phase 3: Fast math and `TexNode`, plus invariant-culture numeric
  readouts. The CSharpMath compatibility spike has been executed and written up in
  full in `DEVIATIONS.md` — the portable `ICanvas` route is proven, glyph identity
  through the bundled face holds exactly, and no SkiaSharp assembly loads — so the
  route is settled and nothing about it is waiting on a decision.

  Deviation 8 is not a question but a pair of implementation obligations, both
  found by running the spike and both producing output that renders and looks
  approximately right while being measurably wrong: CFF glyph bounds must be primed
  per face at load with `UpdateAllCffGlyphBounds()`, and the Y flip is Najm's to
  own and must compose with — not duplicate — the node-level upright flip
  `TextNode` already applies. Read that entry before writing the adapter.

  Resolved 2026-10-06: **Najm adds no LaTeX validation pass.** CSharpMath's parser
  is lenient and silently repairs some malformed input; II.6's "errors fail loud"
  is read as "surface CSharpMath's errors faithfully", and a second partial LaTeX
  grammar inside Najm is explicitly not wanted.

  Scope for the first slice, per resolution 7: equations are **atomic** — one
  recorded `VectorPicture` per formula, portable and vector-exportable. II.6's
  structured walk, atom decomposition and math fragments are a later slice, and
  CS-R2/R3/R4/R5 are the checks that gate it.

- **Phase 4 desktop host landed ahead of its sequence.** `Najm.Host.Desktop`
  ships the window, GL context, event pump, letterboxing with hi-DPI mapping, bar
  clearing, pinned disposal order, `WrapBackbuffer`, F5 warm restart, and full
  input routing with camera-resolved hit testing. It is end-to-end tested on this
  machine under Xvfb against frames captured from the X server. Still absent from
  phase 4: the debug overlay (`F1` is reserved and deliberately swallowed) and
  method-body hot reload. Two host decisions are open for the owner — deviations
  36 and 37.

## Approved scope: phases 1 through 3

### Phase 1 — M0 repository and verification spine

#### Construction

- Initialize a local Git repository without configuring or pushing a remote.
- Pin the .NET 10 SDK and exact external package versions.
- Centralize build and package policy: nullable enabled, deterministic builds,
  warnings as errors for `Najm.Utils` and `Najm.Core`, analyzers, and locked
  restore inputs.
- Create only active projects. The initial graph is:

  - `Najm.Utils` — pure math, color, angle, easing, and curve utilities.
  - `Najm.Core` — portable engine model and rendering contracts.
  - `Najm.Skia` — CPU/GPU Skia realization.
  - Unit-test projects for Utils, Core, and Skia.
  - Architecture-boundary tests.
  - A benchmark project for allocation, traversal, and surface accounting.

- Add `Najm.Lib` and `Najm.Text` when their first complete vertical slices
  begin; do not create empty future Audio, Web, IPC, or native-3D projects.
- Prove exact SkiaSharp, native-assets, Silk.NET, HarfBuzzSharp, and CSharpMath
  compatibility in isolated executable spikes before public APIs depend on
  binding assumptions.
- Build the smallest portable render contract needed to draw one primitive
  through a CPU Skia surface.

#### Gate

- Clean restore and Release build.
- Dependency guards prove Core/Utils do not reference SkiaSharp or Silk.NET.
- One deterministic filled-path raster golden, compared as canonical raw pixel
  data rather than encoded PNG bytes.
- Native Skia and HarfBuzz smoke checks pass on Linux.
- Initial benchmark results are recorded, including target-byte estimates.

### Phase 2 — Headless 2D golden loop

#### Runtime slice

- One-way, engine-controlled scene lifecycle:
  `Construct -> Load -> Start -> (Tick/Render)* -> Stop -> Unload`.
- Fixed and live clock policies with the normative post-advance frame
  convention, double-precision simulated time, long frame indices, and empty
  input in deterministic runs.
- `Node2D`, `Behavior`, `Transform2D`, the registry, and unified deferred
  mutation flushes.
- Deterministic update order, stable `(ZIndex, insertion index)` paint order,
  and depth-first traversal.
- `ScreenLayer`, `WorldLayer2D`, `Camera2D`, virtual resolution, output render
  scale, world/virtual Y conventions, and camera-resolved scale pinning.
- Geometry, hit, and visual bounds sufficient for the active slice, retaining
  distinct contracts so M2 can tighten isolation without redesign.

#### Rendering slice

- Portable Tier-1 paths, text-run hook, and affine images.
- Tier-2 convenience geometry and portable paint/brush descriptors.
- Paths, fills, strokes, gradients, images, clips, transforms, portable blend
  modes, and true group opacity.
- A minimal effect graph sufficient for glow and drop shadow.
- The permanent rendering seams from day one: `ISurfaceProvider`,
  `IRenderTarget`, `ICompositor`, one shared Core traverser, `RenderDirect`, and
  the backend-facing composition bracket SPI.
- M1's ordinary-layer compositor: persistent ordinary layer targets,
  transparent accumulation, ordered merges, and a final replace-blit. It takes
  no snapshots and has no backdrop-read machinery yet.
- Conservative active-clip/full-target unit brackets are acceptable in M1;
  M2 owns tight device-resolved isolation bounds.

#### Motion and delivery

- Minimal scheduler with `NextFrame`, `Seconds`, `For`, FIFO drain-to-empty,
  node/scene ownership, synchronous cancellation disposal, and fault status.
- Tweens driven by the same simulated clock, with the tween pass preceding the
  coroutine pass.
- Fixed-step offline rendering and synchronous PNG output.
- SVG/PDF writers over the direct path.
- Structural vector checks; unsupported constructs fail loudly until their
  correct raster-embed fallback lands.

#### Gate

- Fresh-instance deterministic runs produce identical raw frame hashes in the
  pinned environment.
- Tick once/render twice produces identical pixels and no observable state
  mutation.
- Transform, deferred-mutation, lifecycle, scheduler, and traversal-order
  contract suites pass.
- A representative scene renders at draft, 1080p, and 4K scales.
- Its SVG/PDF contains expected vector structure and no accidental embedded
  raster content.
- Representative warmed core frames meet the allocation target.

### Phase 3 — Baseline text, math, and authoring nodes

#### Construction

- Add the portable Core text model without backend-native fields.
- Add `Najm.Text` as the sole `ITypesetter` producer and `Najm.Lib` for author
  nodes.
- Embed pinned Latin Modern Roman and Latin Modern Math bytes with licenses and
  recorded hashes.
- Shape Latin-oriented text through HarfBuzz in font units, cache immutable
  positioned layouts, and support hard line breaks.
- Implement measured logical/ink bounds, all text anchors, lazy typesetting,
  draw-time color overrides, and the world/screen upright rule.
- Implement `TextNode` and Fast-flavor `TexNode`. Equations may initially be
  atomic, but must remain portable and vector-exportable.
- Implement invariant-culture fixed-format numeric readouts with reusable
  capacity and backend mini-glyph caches.
- Raster text through cached Skia blobs and export text/math as explicit glyph
  outlines and portable rules/paths.

#### Gate

- HarfBuzz kern-pair and ligature checks pass against the bundled font bytes.
- CSharpMath parser/display/measurement/font-identity checks pass for the pinned
  package family, or a reviewed portable-vector adapter replaces that route.
- Anchor, baseline, upright-rule, lazy invalidation, and cache-deduplication
  suites pass.
- Static text performs no typesetter work or managed allocation after warm-up.
- Readouts update without steady-state allocation after capacity is established.
- SVG text/math fixtures contain paths and rules, with no `<text>` or `<image>`;
  PDF fixtures contain neither embedded fonts nor raster images.

## Provisional scope: phases 4 through 6

These phases remain subject to production feedback. Their current direction is
recorded so earlier work preserves the necessary seams without prematurely
implementing them.

### Phase 4 — Desktop live host

- GPU Skia provider over a Silk.NET-owned OpenGL context.
- Manual single-threaded frame loop, letterboxing, DPI mapping, bar clearing,
  flush/present order, resize handling, and pinned disposal order.
- Factory-based F5 warm restart, method-body hot reload, and initial diagnostics.
- CPU-raster tests remain authoritative; hidden-window software-GL smoke tests
  are isolated from deterministic golden tests.

### Phase 5 — External-renderer interaction slice

Most of this phase was executed early, under the cancelled presentation's
deadline, and the parts that landed are retained. The external GL-texture seam is
built and proven: the Mandelbrot sample composites a GLSL ES 3.00 shader through
it, and an independent consumer measured Najm's composite of a wrapped texture as
byte-identical to a direct GL readback. The M2 input mechanisms this phase was to
pull forward — input blocks, camera- and pinning-resolved routing, capture/focus,
dragging, keyboard control — are implemented and routed through the desktop host.

What remains, no longer on a deadline:

- Presentation helpers — chapter graphics, controls, sliders, signal-driven
  stepping — built in an opinionated library against public Core/Lib APIs. See
  TECH-DEBT D-4: `Najm.Lib` is one file, and the taste/correctness split in
  `DESIGN-LANGUAGE.md` governs what may be promoted into the engine.
- The hydrogen renderer boundary specifically. The analysis is done and recorded:
  the one architectural collision is render idempotence, since five of its
  features flip ping-pong buffers inside the draw call, and the fix is to hoist
  that history into scene state advanced at a fixed step in `Update`. Everything
  pedagogically load-bearing in that renderer is already pure and single-draw.
  `~/hydrogen-presentation` pins an older commit of this repository on purpose, so
  its findings describe one fixed floor; adopting any of them means repinning
  first.

### Phase 6 — Production acceptance and hardening

- Orbital identity clip.
- Deterministic sorting visualization. **The live near-term deliverable** — it is
  ROADMAP M1 acceptance production #2, so it is the production that now measures
  M1 rather than a later hardening exercise.
- Publication-quality hybrid/vector figure.
- An interactive presentation slice over an external renderer.
- Use these productions to pull masks, backdrops, richer effects, capture,
  embedded scenes, advanced text, and later native/vector 3D into scope only
  when concrete requirements justify them.

## Explicit deferrals

Until a named production pulls them forward:

- `ReadsBackdrop`, exact backdrop-layer lerp, node-backdrop scratch surfaces,
  masks, tight isolation rectangles, and the full M2 surface pool.
- General vector raster-embed degradation pipelines.
- Live capture, FFmpeg, and audio.
- Rich markup, automatic wrapping, BiDi/script itemization, font fallback,
  fragments, text-on-path, editing/IME, and external dvisvgm math.
- `SceneNode`, deep deck caching/parking, and automatic rude-edit detection.
- Native/vector scientific 3D and GPU volume rendering.
- Web, IPC, and simulation-host integrations.

## Supervisory and delegation model

The primary agent owns public-contract decisions, work decomposition, review,
integration, full-solution verification, and milestone promotion. Subagents own
bounded, independently verifiable areas:

- Core/Utils runtime and semantic tests.
- Skia surfaces, rendering, export, native binding checks, and goldens.
- Text/font/math production and text-specific verification.
- Later, the hydrogen adapter and targeted input/presentation slice.

Concurrent agents receive disjoint file ownership. Shared solution/build files
are changed by one owner at a time. No subagent result is treated as integrated
until the primary agent inspects the diff and reruns the relevant solution-wide
checks.

**Working instructions for any agent, from any tool, live in `AGENTS.md`**, which
`CLAUDE.md` points at so the two cannot drift. It carries the verification
commands, the documentation map, the register disciplines, the engine contracts
that are easy to break, this machine's GL and Xvfb specifics, and the commit and
prose conventions.

**More than one contributor, 2026-10-05.** Work is now also being delegated outside
this machine. Two consequences for the model above:

- `docsref/DEVIATIONS.md` is append-mostly and sequentially numbered, so a
  contributor claims a number block before writing rather than racing for the next
  integer. The shared files named above — `Najm.slnx`, `Directory.Build.props`,
  `Directory.Packages.props`, `global.json` — stay serialized, and a new dependency
  remains the primary agent's decision rather than an implementation detail.
- An outside agent's findings are only worth what reaches the engine, so bounded
  work that explores the authoring experience must produce a written artifact —
  a sample's `NOTES.md`, or a deviation entry — as part of its definition of done.
  A result delivered as a diff alone has lost the half of its value that is hardest
  to recover.

## Specification resolutions

The following resolve gaps found during the planning review unless later
architecture edits supersede them:

1. A still at `at: 0` renders the loaded state with zero ticks. `OnStart` runs
   inside the first tick, not during zero-tick export.
2. `Transform2D.LocalMatrix` uses row-vector
   `Scale * Rotation * Translation`; world composition remains
   `Local * Parent.World`.
3. Easing names use direction first, for example `Ease.OutCubic`.
4. A layer uses a size-free surface profile override for color space and sample
   count. Concrete `SurfaceSpec` remains target-sized.
5. M1 implements ordinary, non-backdrop layer composition. M2 adds masks,
   backdrop reads, exact backdrop lerp, tight isolation, and associated pooling.
6. Fast paths ship only with equivalence tests against the canonical path.
7. Fast math uses `CSharpMath.Rendering` through a Najm-owned portable canvas
   that records an atomic `VectorPicture`; `Najm.Text` does not depend on
   `CSharpMath.SkiaSharp` or SkiaSharp. Structured formula fragments remain a
   later enhancement behind the same portable run boundary.
8. The M1 boundary follows this plan, not `ROADMAP.md`. `ROADMAP.md` places
   desktop live preview, method-body hot reload, manual warm restart, and the
   debug overlay inside M1; all four stay in phase 4. M1 is complete when the
   headless golden loop, baseline text, and offline PNG and vector delivery
   meet the phase 2 and phase 3 gates.

   Consequence for verification: with no live preview, rendered PNG output is
   the only way a change is seen. Acceptance stays visual by rendering
   productions to PNG and inspecting them, and `SkiaExport.Png` is part of the
   working loop rather than a delivery convenience.

   **Amended 2026-10-05.** This resolution also argued the deferral from the claim
   that development runs where "a windowed host cannot run at all". That claim was
   tested and is false — the host runs under Xvfb and is asserted against captured
   X-server frames — and the desktop host has since landed anyway. The phasing of
   the two items still outstanding, hot reload and the debug overlay, therefore
   rests on an argument that no longer holds and should be re-made from scope and
   risk; TECH-DEBT D-6 carries that question. The honest remaining constraint is
   narrower: Xvfb cannot exercise vsync, frame pacing, tearing, focus policy,
   resize-by-dragging, fullscreen or hi-DPI, and llvmpipe timings predict nothing
   about a stage machine. PNG remains the primary acceptance medium, but it is no
   longer the only one available.

## Current execution order

Items 1 through 7 are complete. The spine, both binding spikes, the M0 contracts
and first golden, the headless runtime slice, the compositor and raster delivery,
and the scheduler/tween slice have all landed, along with baseline text and — out
of sequence, under the cancelled deadline — the GPU provider, the external-texture
seam and the desktop host.

What is next, in order:

1. **Continuous integration** (TECH-DEBT D-1). Not a feature, and it goes first
   because every item below it is cheaper to review once a green check exists, and
   because the project's six real invariants are currently defended by memory.
2. **The remaining three sample scenes** — spring–mass–gravity, sine-wave
   construction, Fourier wrapping (`SAMPLES.md` §2, §4, §5). Parallelizable, one
   author each, and the programme that has produced every authoring finding so far.
   Each must leave a `NOTES.md`; that artifact is the deliverable.
3. **The effect path** — glow and drop shadow. Unblocked; the predicate is one
   predicate.
4. **Fast math and `TexNode`, atomic flavor**, then invariant-culture numeric
   readouts. Unblocked; read deviation 8 first. This needs a portable
   `VectorPicture` in Core, which nothing declares yet and which the dvisvgm tier
   (Part IV) and II.6's fallback both also consume — so its shape is a Core
   contract decision, not an implementation detail of the adapter.
5. **SVG/PDF writers and the vector structural check.** Unblocked; `VectorPolicy`
   is deviation 38. Sequenced after math deliberately, so the writers are built
   against real outlined glyphs and rules rather than having text and math retro-fitted.
   This closes phase 2.
6. **The zero-allocation assertion** (TECH-DEBT D-3), **continuous integration's
   second pass** over the GL and host jobs, and **the debug overlay** — together
   these make the M1 exit criteria checkable rather than merely asserted.
7. **`RequestClose()` and `HostOptions.QuitKey`** (deviation 36), which an
   automated end-to-end host run needs in order to stop politely.
8. **M1 acceptance:** the sorting visualization, the orbital identity clip, and the
   publication figure, rendered at draft, 1080p and 4K, with identical replay
   hashes and a passing vector structural check.
9. Phase 1–3 integration review and acceptance report.

Then M2, whose input half is already paid for — the router, camera-resolved hit
testing, capture/focus and dragging all landed with the desktop host. What M2 still
wants is masks and tight isolation bounds, staged accumulation, bulk primitives and
element picking, live capture over the existing `PixelFrameLease`, and the
composition diagnostics. `ROADMAP.md` owns that scope.
