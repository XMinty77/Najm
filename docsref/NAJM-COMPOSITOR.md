# NAJM-COMPOSITOR — Compositor Architecture

**Status.** Current companion to `ARCHITECTURE.md`. This document owns compositor realization: layer accumulation, target and pool ownership, isolation brackets, backdrop plumbing, fast paths, diagnostics, and Skia-specific mechanics. Author-observable semantics remain defined by the engine architecture.

**Structure.** Part I defines the backend-facing contract and invariants. Part II describes the Skia realization.

---

# Part I — Contract

## 1. Placement and seams

Three pieces, three homes:

1. **`ICompositor` (Core).** The composited-path contract. Per-scene and stateful: it owns the persistent per-layer targets and the accumulation surface, holds the debug counters, and is disposed with the scene. Created by the backend via **`ISurfaceProvider.CreateCompositor()`** — the provider is the backend's surface-*and-composition* authority. `Scene.Render(target)` delegates to it; `Scene.Load` acquires it. Shape, indicative:

 ```csharp
 ISurfaceProvider.CreateCompositor() → ICompositor
 ICompositor : IDisposable
 {
 void Render(LayerStack layers, IRenderTarget output, float renderScale);
 CompositorStats Stats { get; } // §9
 CompositorDebugOptions Debug { get; } // §9, DEBUG builds
 }
 ```

2. **The render traverser (Core).** The per-layer tree walk: paint order (`ZIndex`, insertion), bounds/frustum culling, scale-pinning resolution against the layer's camera, and the **§6.7 pipeline orchestration** — evaluating the isolation predicate and driving the context's bracket operations in the normative order. The traverser is invoked *by* the compositor for each layer target, and *by* the direct path for each layer walk; it is the single home of node-tier composition semantics, shared by both paths so they cannot drift.

3. **The direct path (Core).** `Scene.RenderDirect(ctx)` — sequential layer walks into one provided context, per-layer viewport clips, `PushOpacity`, layer `Blend` via a context bracket where expressible, and a **per-layer isolation bracket** when the subtree contains a non-default blend or a backdrop. The bracket is skipped when neither is present. Backend-agnostic by construction; consumed by `VectorExporter` and available as the no-compositing path.

**The backend SPI.** The traverser (Core) drives contexts, and backend compositors drive the traverser, so contexts expose composition operations beyond the author-facing tiers as a **documented backend-facing surface** — public, marked backend-facing in XML docs, and not hidden behind `InternalsVisibleTo`. The operation set the traverser requires of a context:

- `BeginUnit(in UnitParams { boundsHint, opacity, blend, effect })` / `EndUnit()` — the isolation bracket. **All restore-time parameters travel at open; the close is bare.** `EndUnit` composites the unit onto its destination with opacity × blend and applies the unit's `Effect` graph at the close (decal sampling beyond the unit image) — there is no separate `ApplyUnitEffect` operation; the graph is a `UnitParams` field, applied by the realization at close (Skia: on the `saveLayer` restore paint).
- `BeginMask(MaskChannel channel, bool invert)` / `EndMask()` — the mask construct, parameters likewise at open: mask children paint into it in their own paint order (`SrcOver` among themselves); `EndMask` extracts the channel, applies inversion (`m′ = 1 − m`), and multiplies the open unit.
- `ApplyBackdrop(EffectGraph, deviceRegion)` — the backdrop-replace construct against the current destination: region contents replaced by `graph(dest)`, unconditional, **before** the unit composites. Region = resolved subtree geometry ∩ active `Clip`, computed by the traverser. It is a self-contained construct rather than an open/close pair.

**Why parameters bind at open:** Skia's `saveLayer` model — and any layer-restore backend — binds the restore paint (alpha, blend, image filter, mask color filter) **when the layer opens**; there is no retroactive attachment. The traverser reads all of these off the node before opening the bracket anyway, so passing them at open costs nothing and deletes a realization impossibility. A future scratch-target backend is free to ignore the parameters until close; the SPI merely guarantees they are available at open.

The compositor requires the following surface operations: **create target** (spec'd), **clear**, **replace-draw** (`Src`, color-space-converting via surface tags), **composite-draw** (opacity × blend from the portable subset, into a destination rect), **filtered draw** (an `EffectGraph` applied during the draw), **lerp-merge** (`out = dst·(1−α) + src·α`, uniform α, premultiplied — §6), and **read-while-write surface-to-surface draw** that never materializes an intermediate image (§7, the CoW rule).

## 2. The canonical composited path (normative algorithm)

Definitions: **O** — the host-provided output target, treated as write-only; **A** — the compositor-owned **accumulation surface**, allocated at `ceil(VirtualResolution × RenderScale)` in **O's `SurfaceSpec`** (making A the §3.4 merge space by construction), persistent, re-acquired on size/spec change, **cleared to transparent every render** (targets clear every render; the scene's background is the bottom layer's `ClearColor`).

**O may be a content-rect adapter.** **O** may be an adapter over a larger native surface — canonically the desktop host's letterboxed backbuffer wrap (`WrapBackbuffer`, NAJM-SKIA I.6) — whose `Size` reports the **content rect**, whose context arrives pre-translated and pre-clipped to that rect, and whose `Snapshot()` crops to it. The compositor's final 1:1 replace-blit and FP-1's direct traversal are expressed in O's coordinates and land correctly through the adapter; the `Src` blit under the adapter's clip is a clipped replace, which is the intended semantics (letterbox bars are outside O and are host-cleared). A is sized from `O.Size` — i.e., the content rect — which coincides with `ceil(VirtualResolution × RenderScale)` by the host's letterbox math. This keeps `ICompositor.Render(layers, output, renderScale)` free of placement parameters; the alternative (an offset argument) would smear letterboxing — a host concern (§5.1) — into every compositor realization and into FP-1.

Per render:

1. **Partition** the visible layers, bottom → top, into **runs split before each `ReadsBackdrop` layer**. Zero RB layers ⇒ one run; the staging machinery costs nothing when unused.
2. **For each run**, per layer in order:
 a. **Bind** the layer's persistent target (acquire on first use; re-acquire per §5.3 triggers, including viewport sizing). **Clear** to `ClearColor`.
 b. Install the base transform (`RenderScale` × camera mapping); run `OnBeforeRender` → **traverser** → `OnAfterRender` in the architecture §5.3 order.
 c. **Merge into A:** draw the layer target into A — `Layer.Effect` applied during the draw if set (decal edges) — composited with `Opacity` × `Layer.Blend` into the viewport rect if set (1:1 placement, no resampling), color-converted into A's space by the tagged-surface draw.
3. **At each `ReadsBackdrop` layer** (a run boundary):
 a. **Initialize the destination layer target from source A** using a `Src` surface draw, converted into the layer's tagged color space. For a viewport layer, select A's matching source region; source and destination pixel sizes are equal, so the copy is 1:1.
 b. Render the layer normally (step 2b).
 c. **Lerp-merge into A:** `out = A·(1−Opacity) + layer·Opacity`, with `Layer.Effect` applied to the layer content during the draw if set; `Layer.Blend` ignored (§5.3). Outside the layer's region, A is untouched.
4. **Final blit:** replace-draw A → O, 1:1. O's prior contents never matter; determinism holds regardless of what the host hands us.

**Ordering freedom, stated:** *within a run*, whether layer targets are all rendered first and then merged, or rendered-and-merged interleaved, is realization freedom — the observable result is identical; the difference is GL surface-switch and flush counts (Part II §2). *Across a run boundary* there is no freedom: the merge-so-far must be complete before an RB layer binds.

**Degeneracies (all normative):** an invisible layer is skipped entirely — not bound, not cleared, not merged. A layer with `Opacity == 0` is likewise skipped (for an RB layer, the lerp degenerates to `out = below`; skipping is exact). A layer whose root subtree culls to nothing still binds and clears (its `ClearColor` is content) but its traversal is empty.

**Idempotence** (§4.1) holds by construction: A and every layer target are pure functions of this frame's scene state; the RB initialization reads this frame's below-merge, never a prior render; first-use target acquisition is the exempted benign memoization.

## 3. Fast paths

Each fast path has an explicit predicate and a byte-equivalence test against the canonical path.

- **FP-1 — single layer directly to output.** Exactly one visible full-frame layer, opacity one, default blend, no layer effect/backdrop read, and normalized `SurfaceSpec` equal to the output. This skips the layer target, accumulation surface, and final blits.
- **FP-2 — verified atomic-node paint folding.** A built-in node that declares `CompositionAtomicity.SinglePrimitive` may fold opacity and a compatible effect into its one primitive paint. Arbitrary custom drawables and child count alone never qualify.
- **FP-3 — clip-only inline path.** `Clip` is canvas state and does not isolate by itself.
- **FP-4 — invisible or zero-opacity layer skip.**
- **FP-5 — direct-path layer bracket skip.** A layer with no composition-active nodes walks inline.

Progressive merge into the output is deferred. Damage tracking is not inferred by the compositor; `SceneNode.RenderPolicy` is explicit at the embedding boundary.

## 4. Isolation brackets

**Predicate:** bracket when blend is non-default, a mask/effect/backdrop is active, opacity is below one, or `Isolate` is true. The only exception is the verified atomic-node fast path above.

```text
PushClip(node.Clip)
[ApplyBackdrop(graph, resolvedGeometry ∩ clip)]
[BeginUnit(visualBoundsHint, opacity, blend, effect)]
 render node and children in paint order
 [BeginMask(channel, invert) … EndMask()]
[EndUnit()]
PopClip
```

Backdrop executes against the true destination before the unit bracket opens. Because the unit is isolated, this operational order preserves the semantic result: the replacement precedes the unit composite and is not modulated by unit opacity.

**Sizing:** resolve `VisualBounds` through the active camera and pinning frame, intersect with the active clip, apply the Core effect bounds transform, snap outward to device pixels, and add a small backend safety epsilon. Unknown or unbounded visual output uses the active clip. The semantic backdrop region is calculated independently from resolved subtree geometry and never expands merely because an effect halo does.

A bracket inherits the enclosing target's full normalized `SurfaceSpec` and draws back 1:1. A default bracket must be byte-identical to inline rendering. Nesting is LIFO; peak depth and isolated pixel area are diagnostics.

A future non-Skia backend may realize units through provider-pooled scratch targets. Shipping Skia contexts use native layer constructs where conforming.

## 5. Masks

Mask children composite among themselves `SrcOver` in their own paint order into the mask construct; `EndMask` extracts `MaskChannel` (`Alpha` | `Luminance`), applies `Invert` after extraction, and multiplies the open unit. Two contract notes:

- **Extent-independence of `Invert`:** the mask multiplies the *unit*, and the unit is transparent outside its own content; therefore an inverted mask's result is independent of the bracket's extent (full-bounds vs. tight later). There is no observable coupling to the bracket bounds.
- **Masks never allocate scratch on Skia** (Part II §3) — the construct is the nested-`saveLayer` idiom. The portable recipe (§4) is the only scratch consumer, and it doesn't ship.

## 6. `ReadsBackdrop` plumbing and the lerp

The RB layer's init and merge are steps 3a/3c of §2. The **lerp is not source-over** and must be realized exactly: with premultiplied colors and uniform `α = Opacity`,

```
out = dst · (1 − α) + src · α # src = the (optionally Effect-filtered) layer content
```

Where the below-merge is transparent, `SrcOver`-with-alpha diverges from this (it under-attenuates dst); the §10 lerp golden includes a transparent-below case precisely to catch a `SrcOver` realization. Two sanctioned realizations, backend's choice (Part II §5 picks): **two-pass** — attenuate dst by `(1−α)` via a destination-in fill over the merge region, then add the α-scaled source (`Plus`) — exact, order-fixed, ancient API; or **one-pass runtime blender** where the backend has one. The merge region is the layer's viewport rect (full A otherwise); outside it, A is untouched; the region edge is hard by viewport semantics.

`Layer.Blend` is ignored on RB layers (§5.3 — the layer *is* the composite). Fading `Opacity` crossfades toward the unmodified below-content — the constitution's intended semantics, delivered by the math above.

## 7. Targets, pool, and accounting

The surface pool is provider-owned and environment-lifetime, shared by every compositor including embedded scenes. Compositors own persistent layer targets and one accumulation surface; scratch surfaces are leased from the provider pool.

Pool keys include bucketed dimensions and normalized `SurfaceSpec`. Entries are returned in the same render call, trimmed by provider render epochs, and evicted under a soft cap. Resize storms and first-seen topologies are transition events rather than steady-state failures.

Memory reporting is explicitly **estimated resident color-storage bytes**, not an assertion of exact driver allocation:

```text
estimatedBytes = width × height × bytesPerPixel × effectiveSampleCount
```

The estimate includes sample count for multisampled targets and reports mip/stencil/backend overhead as unavailable unless the backend exposes it. Provider surfaces and Skia's internal cache remain separate accounting domains.

The node-backdrop scratch construct and vector-export raster staging are initial pool clients. Stable topology must show no acquire/miss/evict/trim events after warm-up.

## 8. The CoW rule (normative for every realization)

**The canonical loop takes zero snapshots.** Every read of a surface still being written this frame — layer target → A, A → RB init, A → O — goes through the read-while-write surface draw (§1), never through snapshot-then-draw. A snapshot of a surface that is subsequently written forces a copy-on-write duplication of the whole backing; with one RB layer per frame that is a hidden full-frame copy per frame. `Snapshot()` remains for genuine image consumers (capture tees, `SceneNode`'s transient `Always` path, author code); the compositor itself never calls it.

## 9. Instrumentation and debug hooks

**Counters** (`CompositorStats`, cheap fields, read by the §15 overlay): composition brackets opened per frame, itemized (unit / mask / backdrop constructs) — author Tier-3 `saveLayer`s excluded by definition; peak bracket nesting depth; RB barrier count; layer-target count and bytes; A's size; pool in-use and cached bytes; pool events this frame (acquire, miss-create, evict, trim). Counter increments live in the traverser (bracket ops) and the compositor (merge, pool) — one owner per number.

**Hooks** (`CompositorDebugOptions`, DEBUG builds): **`ForceBracket`** — the isolation predicate is forced true for every node (powers the no-op equivalence test); **`ForceCanonicalPath`** — FP-1 disabled (powers path-equivalence). Both are frame-coherent (read once at render start).

## 10. Test obligations (Part I)

Cross-referenced to architecture §18. Every `ICompositor` realization must pass: isolation **no-op equivalence** (inline vs. `ForceBracket`, byte-identical); **pipeline-order golden** (glow halos the masked result; nested inverse bounds it); **`ReadsBackdrop` idempotence** (tick once, render twice, identical hashes); **invert-lens golden**; **fast ≡ canonical** (FP-1-eligible scene, byte-identical under `ForceCanonicalPath`); **direct-path blend-scope golden** (an upper-layer `Multiply` node never samples lower layers); **lerp golden** against the closed form, including transparent below-merge; **`Backdrop` region golden** (frost confined to resolved subtree geometry ∩ active clip, shaped by clip); **`Backdrop`-independence golden** (unit `Opacity` does not fade the replaced backdrop); **viewport 1:1 crispness** (a pixel-grid test pattern survives placement exactly); **determinism** (two fresh-instance fixed-step replays hash identically with an RB layer in the stack, per pinned environment); **steady-state pool stability** (N warm frames ⇒ zero managed allocation, zero pool events). Additionally, the **no-op equivalence** and **fast ≡ canonical** tests run under a **GPU MSAA-4 configuration**: bracket `saveLayer`s must inherit the enclosing surface's sample count for byte-identity to hold, and this is the test that proves it (binding check SK-R05, NAJM-SKIA Appendix A) — the one place where Skia-internal behavior is load-bearing for a contract test.

---

# Part II — SkiaCompositor realization

Written against SkiaSharp 3.x directly; no abstraction apologies. Everything here realizes Part I; nothing here is author-observable beyond it.

## 1. Anatomy

`SkiaCompositor` (in `Najm.Skia`) holds: per-layer `SKSurface`s and A (acquired through the provider so accounting is uniform, though they are persistent, not pooled); the staged loop of Part I §2; the stats block. All surfaces originate from the host's `GRContext` (GL) or raster allocation (offline) — one context, one thread, per §3.5 — and every provider-created GPU surface is **`budgeted: false`**: the engine accounts its own surfaces; Skia's resource-cache budget is reserved for Skia-internal allocations. The traverser is Core code invoked with the layer target's `SkiaDrawContext2D`; the compositor never re-implements node semantics.

## 2. Surface traffic and flush economics

Default run ordering: **render all of a run's layer targets first, then merge the run into A** — this minimizes render-target switches on GL (each switch forces an internal flush; §4.7 already treats mid-render switches as measured-territory). Interleaved render-and-merge is contractually equivalent (Part I §2) and may win on memory locality for CPU raster; switch only on profile evidence. Run boundaries (RB layers) serialize by contract — no freedom there.

All surface-to-surface reads use **`SKCanvas.DrawSurface(surface, x, y[, sampling], paint)`** — the read-while-write primitive, **API-confirmed with that exact spelling on the pinned binding (registry in NAJM-SKIA Appendix A)**; the paint carries the merge parameters (alpha, blend mode, color filter, image filter). Tagged surfaces make every such draw color-space-converting for free (§3.4). Per the Part I §8 CoW rule, the loop contains no `Snapshot()` call — the zero-snapshot loop stands as written; that GPU→GPU surface draws take the texture path remains a NAJM-SKIA binding note.

## 3. Bracket realization — the `saveLayer` mapping

Skia contexts realize every Part I §4 construct natively; **no scratch surfaces are involved**:

- `BeginUnit(in UnitParams)` → assemble the restore paint **at open** from the params (Skia binds the restore paint when the layer opens; there is no retroactive attachment): `Alphaf = Opacity`, `BlendMode = Blend`, `ImageFilter =` the lowered `Effect` graph → `saveLayer(boundsHint /* resolved visual bounds */, restorePaint)`. `EndUnit()` → `Restore()` — so §6.7 steps 4 and 5 ride one restore. `SKCanvas.SaveLayer(in SKCanvasSaveLayerRec { Bounds, Paint, Backdrop, Flags })` **exists as written** (API-confirmed) for the variants that need the full rec.
- `BeginMask(channel, invert)` → nested `saveLayer` whose restore paint, assembled at open, is `BlendMode = DstIn` plus a color filter: `Luminance` → Skia's luma filter; `Invert` → an alpha-inverting color matrix (`a′ = 1 − a`), composed **after** channel extraction per §6.7. `EndMask()` → `Restore()`, which multiplies the enclosing unit — the classic Skia idiom; mask children draw normally inside.
- A node that explicitly declares `CompositionAtomicity.SinglePrimitive` may fold a compatible effect or opacity into its one paint. Arbitrary drawables never receive this shortcut.
- **Effect-after-mask order falls out of nesting:** unit layer opens → content → mask layer opens/closes (multiplies) → unit restore applies `ImageFilter` then composites with opacity/blend. Exactly the §6.7 pipeline, one construct per step.

Filter lowering itself (graph → `SKImageFilter` DAG, decal crop rects, CTM interaction with pinning) is NAJM-SKIA's; this document consumes it.

## 4. `Backdrop` (node-tier) realization

The backdrop replacement must be unconditional and unmodulated by the unit. Therefore, Skia's backdrop-`saveLayer` alone is **not** conforming (it folds the filtered backdrop into the unit's layer). The realization is two constructs — the backdrop-replace, then the unit's ordinary bracket — executed in the operational order (replace *before* the unit's bracket opens).

**The pooled-scratch construct is the normative realization.** For region `R = resolved subtree geometry ∩ active clip` (device-resolved by the traverser):

1. **Acquire** a scratch at the region **outset by the graph's bounds transform**, in the destination's spec.
2. **Copy** the destination's outset region into it via `DrawSurface` (read-while-write — no snapshot, Part I §8), **clamp-padding** the margin where the outset exceeds the destination surface (four stretched one-pixel edge strips plus explicit corner fills, realizing edge clamp exactly).
3. **Write back:** draw the scratch onto the destination through the lowered graph with `BlendMode = Src`, clipped to the region (the active clip path plus the resolved geometry device rect).
4. **Release** the scratch same-frame.
5. **The unit's ordinary bracket** (§3 above) then composites over the replaced destination with `Opacity` × `Blend`.

**Why the inversion (worked scenario NAJM-SKIA Appendix B, S1):** a frosted panel's blur must *sample* the true destination beyond `resolved subtree geometry ∩ active clip` but *write* only within it. The rec construct offers one knob — the clip — for both the backdrop snapshot's extent and the `Src`-restore's write extent: clip to the region and the blur samples clamped-at-region (wrong, visible vignetting at the panel edge); clip to the outset and the `Src` restore replaces the outset ring (wrong, destination destroyed outside the panel). The two requirements are jointly inexpressible in one `saveLayer`; the scratch construct expresses them trivially, and its cost (one bounded copy per backdrop unit per frame) is exactly what the pool was built for — hence the pool's unconditional day-one customer (Part I §7).

**The rec-based backdrop-`saveLayer` is a demoted, optional fast variant** — `saveLayer(SKCanvasSaveLayerRec { Backdrop = loweredGraph, Paint = { BlendMode = Src } })` under a region clip, restored immediately — **legal only for pointwise graphs** (identity bounds transform — `ColorMatrix`/`Tint` and compositions thereof), where no outset exists and the two constructs are pixel-identical. The rec type **exists as written** (API-confirmed); the variant's *behavioral* preconditions remain runtime checks: **SK-R01** (`Src`-restore yields replace-not-over on semi-transparent destinations), **SK-R02** (unit `Opacity` demonstrably does not fade the replaced backdrop — the §10 independence golden), **SK-R03** (tile-mode behavior at region edges). It is an optimization, never a requirement; the scratch construct is unaffected by the checks' verdicts.

## 5. `ReadsBackdrop` realization

- **Init:** `layerTarget.Canvas.DrawSurface(A, sourceRect, destinationRect, paint { BlendMode = Src })`. A is the source and the layer target is the destination. Surface tags perform color conversion; viewport regions copy 1:1.
- **Lerp merge, normative realization: the two-pass.** Pass 1 — fill the merge region on A with a transparent-black paint at `Alphaf = 1 − α`, `BlendMode = DstIn` (dst ×= (1−α), color and alpha, premul-correct). Pass 2 — `DrawSurface(layer, …)` with `Alphaf = α`, `BlendMode = Plus`, `ImageFilter =` the layer's `Effect` if set (filter → alpha → blend is Skia's paint pipeline order, matching §5.3's snapshot → Effect → composite). Exact against the closed form, including transparent below; requires nothing newer than Porter-Duff.
- **One-pass alternative:** an `SKBlender` (`return src*α + dst*(1−α);` with a uniform — src/dst arrive premultiplied and the return is written raw, so this is the whole shader). `SKBlender`, `SKPaint.Blender`, and `SKRuntimeEffect.CreateBlender` **exist at the API level** (confirmed against the pinned SkiaSharp 3.x sources); the **two-pass `DstIn`+`Plus` remains normative** until the one-pass blender passes the lerp golden on the pinned version (runtime check **SK-R04**) — then it may be adopted as an equivalent realization per Part I §6. The two-pass stays normative because it has zero binding risk and the lerp golden pins both to the same math.

## 6. Fast path FP-1

The traverser is pointed at O's context directly; node brackets `saveLayer` on O's canvas exactly as on a layer target (the spec-match predicate is what makes this byte-equivalent — same MSAA, same color space, same AA rasterization). `ForceCanonicalPath` swaps this decision off for the equivalence test.

## 7. CPU raster (OfflineRenderer) notes

Identical code path. **Raster providers normalize `SurfaceSpec` sample counts to 1 at target creation** — CPU raster is analytically antialiased and sample counts are meaningless there — and spec-match predicates (FP-1's full-spec match, bracket spec inheritance) compare the *normalized* spec, so offline configurations behave as if everything is 1-sample. Surface draws are SIMD blits; the final A → O blit is a memcpy-grade pass (~8 MB at 1080p) invisible next to PNG/ffmpeg encode; no flush economics apply, so the §2 run-ordering choice is free — keep the GL-optimal default for one code path. Determinism hashes are taken on this configuration (§2.2, per pinned environment).

## 8. Pool realization

An `SKSurface` pool keyed per Part I §7 — key equality is (bucketed W, bucketed H, `SKColorType`, colorspace, sample count), sample counts normalized per §7. Stacks of idle surfaces per key; the **render-pass epoch trim** (Part I §7) walks idle entries' last-used stamps during the epoch increment. Every pool surface — like every provider-created surface — is `budgeted: false`. **Boundary with Skia's own caching, stated once:** `saveLayer` allocations are internal to Skia and ride the `GRContext` resource cache — they are *not* pool traffic and must not be double-budgeted; the `GRContext` cache limit is a host/NAJM-SKIA knob that, because provider surfaces are unbudgeted, governs Skia-internal allocations only. Our pool budgets only surfaces we explicitly create (the backdrop scratch — the unconditional day-one customer, vector-export staging; future customers).

## 9. Failure and storms

`CreateTarget` or surface-allocation failure propagates to the driver. A presentation host that must survive may catch at its driver boundary. Resize storms reacquire on each settled size, release superseded surfaces immediately, and let epoch trimming collect pool residue; no hysteresis is built initially.

---

## Appendix A — Deferred optimizations

- Progressive merge directly into eligible outputs.
- One-pass runtime-blender realization of the exact backdrop-layer lerp after behavioral tests.
- Target parking/eviction hooks for large presentation decks.
- Portable scratch-bracket implementation when a non-Skia backend exists.
- Host-side render-scale quantization only if profiling justifies the observable trade-off.
