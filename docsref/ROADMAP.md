# Najm — Implementation Roadmap

**Purpose.** This roadmap converts the architecture into evidence-driven vertical slices. A milestone is complete only when its acceptance clips and tests work end to end. Contracts not required by the active milestone remain architectural direction, not reasons to delay a usable renderer.

## Roadmap principles

1. **Prove the golden loop first.** Live preview, deterministic render, and vector export of the same polished 2D material are more valuable than broad subsystem coverage.
2. **Implement semantics before optimizations.** Fast paths require byte-equivalence tests and measured benefit.
3. **Pull advanced features with real productions.** Multilingual typography, deep presentation nesting, backdrop reads, native 3D, and IPC land when a concrete clip or demo needs them.
4. **Keep acceptance visual.** Every milestone ships representative samples, golden frames, and diagnostics—not only APIs.

## M0 — Repository and test spine

**Scope**

- Solution/projects, dependency guards, pinned SDK/packages.
- Core math/color/easing utilities and architecture-boundary tests.
- Minimal raster test target and golden-image harness.
- Benchmark harness for managed allocation, traversal, and surface accounting.

**Exit**

- Clean build and architecture guards in CI.
- One deterministic raster primitive golden.
- Baseline benchmark results recorded rather than guessed.

## M1 — Golden loop: first usable Najm

**Goal:** produce basic, vector-crisp, gorgeous 2D animations immediately.

**Scope**

- `Scene` lifecycle through engine-controlled commands and protected hooks.
- `Node2D`, `Transform2D`, registry/deferred mutation, one world layer and optional HUD/screen layer.
- Camera, virtual resolution, render scale, letterboxing.
- Paths, fills, strokes, gradients, images, core blend subset, clip, group opacity, and a small effect path such as glow/drop shadow.
- Baseline text: plain `TextNode`, `TexNode`, measured bounds, bundled fonts, outlined vector export.
- Minimal choreography: `Wait.NextFrame`, `Wait.Seconds`, `Wait.For`, basic `Animate`, cancellation.
- Desktop live preview; fixed-step offline PNG sequence; SVG/PDF still export.
- Method-body hot reload plus manual `F5` warm restart.
- Debug overlay for frame time, allocations, node count, bounds, brackets, and target estimates.

**Deliberately absent**

- `Transform3D`, input routing/widgets, rich text, full coroutine vocabulary, `ReadsBackdrop`, embedded scenes, automatic rude-edit detection, audio, IPC.

**Acceptance productions**

1. **Orbital identity clip:** animated curves/gradients, crisp labels and equations, glow used sparingly, rendered live and to PNG.
2. **Sorting or Fourier clip:** reusable data-driven choreography with deterministic output.
3. **Publication figure:** the same visual language exported to SVG/PDF with outlined text and no accidental rasterization.

**Exit criteria**

- All three productions look intentional at 360p draft, 1080p, and 4K render scale.
- Fixed-step replays hash identically in the pinned raster environment.
- The vector structural check confirms expected vector content and detects accidental rasterization.
- Representative steady-state core frames allocate zero managed bytes after warm-up.

## M2 — Composition and interaction

**Scope**

- Full layer compositor with staged accumulation.
- Tight visual-bounds isolation, masks, effect graphs, layer effects/blends.
- Node backdrop; add `ReadsBackdrop` only when a production needs cross-layer feedback.
- Full input block/router, camera-resolved hit testing, capture/focus, dragging.
- Bulk 2D primitives and element picking.
- FFmpeg sink and live capture through owned pixel-frame leases.
- Composition counters, resolved-bounds overlays, and vector-degradation diagnostics.

**Acceptance productions**

- Slider-over-world visualization.
- Draggable point constrained to a curve.
- Masked gradient title with glow.
- Frosted or inverted lens, pulling `ReadsBackdrop` into scope only if required.

## M3 — Authoring ergonomics and scene composition

**Scope**

- Full coroutine/signal semantics and replay utilities.
- Arrangement helpers and reusable Guard components.
- `SceneNode` with `RenderPolicy.Always`, `WhenInvalidated`, and `Once`.
- Explicit target parking/invalidation for decks.
- Text-on-path, basic fragments/overlays, and richer math decomposition.
- Audio cue recording/device sink where a production needs it.
- Optional automatic rude-edit detection after an isolated runtime spike proves reliability.

**Acceptance productions**

- Embedded retimed scene with cached static diagram.
- Small interactive presentation with forward/back replay.
- Animated text following a curve without per-frame shaping.

## M4 — Scientific 3D

**Scope**

- `Transform3D`, `Camera3D`, projection backend, 3D-to-2D interoperability.
- Point/line/splat batches, scalar transfer functions, depth cues, additive and true-MIP composition.
- Anchored 2D labels and 3D vector export.
- Orbit/fly controls.

**Acceptance productions**

- Animated lattice with 2D controls.
- 40k-point hydrogen-orbital/probability visualization meeting the baseline laptop budget.
- Publication-quality vector 3D figure.

## M5 — Advanced text, presentations, and external data

**Demand-pulled scope**

- Rich text/wrapping/fragments; BiDi, fallback, and complex scripts when required.
- External dvisvgm Full math decorator.
- Deep deck focus path, parking/eviction, and target-budget policies.
- Signal-log tooling and presentation/deck utilities.
- IPC/shared-memory simulation snapshots and parameter feedback.
- Web-compatibility audit and future backend seams.

## Promotion rules

A deferred contract becomes scheduled when at least one of these is true:

- a named acceptance production cannot be built cleanly without it;
- profiling identifies a measured bottleneck with a reproducible benchmark;
- a portability/export failure demonstrates that the current seam is insufficient;
- two separate authoring examples independently need the same abstraction.

Until then, prefer a local, explicit implementation in `Najm.Guard` over expanding Core.
