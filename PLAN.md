# Najm Construction Plan

**Status:** Living implementation plan  
**Last updated:** 2026-08-11  
**Source of truth:** `docsref/BRIEF.md`, `docsref/ARCHITECTURE.md`,
`docsref/NAJM-COMPOSITOR.md`, `docsref/NAJM-SKIA.md`,
`docsref/NAJM-TEXT.md`, and `docsref/ROADMAP.md`.

This plan turns the architecture into vertical implementation slices. The
architecture documents own semantics; this document owns execution order,
acceptance gates, delegation, and current scope.

## Product priorities

Najm is a vector-first procedural graphics engine for educational animation,
interactive presentations, and publication figures. Its defining combination
is a game-engine-style scene graph, imperative immediate-mode drawing,
Illustrator-like composition, serious text and mathematics, deterministic
offline output, and live interaction.

The first productions that guide implementation are:

1. An interactive hydrogen-atom presentation, with an existing GLSL ES
   renderer embedded beneath crisp Najm-authored presentation graphics.
2. A sorting-algorithm animation with reusable, deterministic choreography.
3. An orbital identity clip and a publication-quality vector figure that prove
   raster and vector delivery from the same scene model.

2D is the priority. The existing hydrogen renderer will be integrated through
the external GL-texture seam before Najm's native scientific 3D milestone.

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
- **Phase 2 in progress:** fixed/live timing, the detached 2D tree/logical
  transform foundation, engine-controlled scene lifecycle, layers/behaviors,
  identity registration, projected FIFO deferred mutation, and allocation-free
  Update traversal are implemented. The portable drawing surface now includes
  paths, affine images, clips, transforms, true group opacity, tagged clear,
  and the portable blend subset through the CPU Skia backend. World layers,
  cameras, the shared render traverser, composition, and delivery remain.
- **Phase 3 foundation landed early:** pinned Latin Modern Roman/Math assets,
  provenance/licenses, and the internal HarfBuzz ownership/shaping path are in
  place, with explicit native lifetime, thread-affinity, pool reset/reuse, and
  RTL-cluster coverage. Public text layout and math-picture contracts remain
  sequenced after their Core geometry/rendering prerequisites.

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

### Phase 5 — Hydrogen-priority interaction slice

- Audit and extract the reusable renderer boundary from the existing hydrogen
  C#/Silk.NET host.
- Render hydrogen into a same-context GL texture and wrap it through Najm's
  external-texture `IImage` seam.
- Compose crisp Najm text, equations, controls, and chapter graphics above it.
- Pull forward only the required M2 input mechanisms: input blocks, camera- and
  pinning-resolved routing, capture/focus, dragging, keyboard controls, sliders,
  and signal-driven stepping.
- Build presentation helpers in `Najm.Guard` against public Core/Lib APIs.

### Phase 6 — Production acceptance and hardening

- Orbital identity clip.
- Deterministic sorting visualization.
- Publication-quality hybrid/vector figure.
- Interactive hydrogen presentation slice.
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

## Current execution order

1. Repository/test spine.
2. Skia/Silk executable binding spike.
3. HarfBuzz/CSharpMath executable compatibility spike.
4. M0 portable contracts and first raw-pixel golden.
5. Headless scene/runtime slice.
6. Ordinary compositor and offline/vector delivery.
7. Minimal scheduler/tween slice and sorting acceptance fixture.
8. Baseline text, Fast math, and numeric readouts.
9. Phase 1–3 integration review and acceptance report.
