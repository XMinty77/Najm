# NAJM-SKIA — Skia Backend & Desktop Host Architecture

**Status.** Current Skia and desktop-host realization for `ARCHITECTURE.md` and `NAJM-COMPOSITOR.md`. It owns surfaces and providers, drawing/effect/text lowering, vector export, GL host integration, capture, media sinks, diagnostics, and binding checks.

**Structure.** Parts I–III are host-independent Skia realization; Part IV is the desktop host; Part V covers offline drivers and sinks; Part VI records threading, failure, and performance constraints.

---

## 0. Pinned dependencies and the binding-check discipline

| Package | Pin | Role |
|---|---|---|
| **SkiaSharp** | 3.x line; exact version in `Directory.Packages.props` | The backend. Exact APIs are pinned by the Appendix A binding-check registry, which reruns on every dependency update. |
| **SkiaSharp.NativeAssets.Linux / .macOS / .Win32** | matches SkiaSharp | Native Skia. |
| **Silk.NET.Windowing + Silk.NET.Input + Silk.NET.OpenGL** | 2.x (≥ 2.21) | Window, GL context, event source, proc loader (Part IV). Silk.NET 3.x is a watch item, not a target. |
| **Svg.Skia** | current, SkiaSharp-3-compatible | SVG *asset decoding* only (I.8). SVG *export* is SkiaSharp's own `SKSvgCanvas` — no dependency. |
| **FFmpeg** | external binary, discovered on `PATH` or via `HostOptions`/sink option | `FrameSink.FfmpegPipe` spawns it; never linked. |

**Binding-check discipline.** Skia's API surface is verified by executable binding checks (Appendix A). Skia's *behavior* — restore semantics on semi-transparent destinations, vector-canvas construct emission, sample-count inheritance — is pinned by **runtime checks** (`SK-R##`), each a small test with a stated fallback, run once per pinned SkiaSharp version. A realization in this document that depends on a runtime check names it inline. This is the same honesty device the constitution uses for cross-platform determinism (§2.2): pin the environment, test the pin.

---

# Part I — The provider layer (`Najm.Skia`)

## I.1 `SurfaceSpec` realization

`SurfaceSpec` (Core: width, height, sample count, color space — §4.5) lowers as follows:

| `ColorSpace` | `SKColorType` | `SKAlphaType` | `SKColorSpace` | Base bytes/px |
|---|---|---|---|---|
| `Srgb` | `Rgba8888` | `Premul` | `SKColorSpace.CreateSrgb()` | 4 |
| `LinearSrgb` | `RgbaF16` | `Premul` | `SKColorSpace.CreateSrgbLinear()` | 8 |

- **Every surface is tagged** — the `SKColorSpace` is passed at creation, never null (§3.4: untagged surfaces do not exist). Tagging is what makes every `DrawSurface` merge color-converting for free (NAJM-COMPOSITOR II §2).
- **Premultiplied alpha everywhere** past the API boundary (§3.4). The only unpremultiplied bytes in the system are `CopyPixels(Rgba8888)` outputs and encoded assets before decode.
- **`SKSurfaceProperties`:** pixel geometry **`Unknown`** on every engine surface — grayscale-AA text, no LCD subpixel rendering, anywhere. LCD text is wrong on transparent offscreen layers and non-deterministic across merges; one uniform choice keeps the parity and determinism suites honest (see the parity and determinism checks in Appendix A).
- **Origins:** all offscreen surfaces are **`TopLeft`**; the wrapped window framebuffer is **`BottomLeft`** (GL reality — I.6). Skia tracks origin per surface and reconciles automatically on `DrawSurface`/snapshot; no engine code flips y (§3.2's "the flip lives in cameras" stays true — this flip is Skia-internal plumbing, invisible above the provider).
- **Sample counts:** GPU surfaces honor the spec's count (`SKSurface.Create(grContext, budgeted, info, sampleCount, origin, props)`); **raster providers normalize to 1** — CPU raster is analytically antialiased and the axis is meaningless there. Spec equality (FP-1, bracket inheritance) compares normalized specs.

## I.2 The provider family

```
SkiaSurfaceProvider (abstract, Najm.Skia) : ISurfaceProvider
 ├─ owns: SkiaSurfacePool (I.5), accounting (per-client bytes/stats), CreateCompositor() → SkiaCompositor
 ├─ CreateTarget(in SurfaceSpec) → SkiaRenderTarget (template; calls CreateSurface)
 └─ abstract CreateSurface(in SurfaceSpec) → SKSurface
 ├─ RasterSkiaSurfaceProvider : SKSurface.Create(SKImageInfo, props) — CPU, offline (§2.4)
 └─ GpuSkiaSurfaceProvider(GRContext, bool ownsContext)
 : SKSurface.Create(grContext, budgeted: false, info, samples, TopLeft, props)
 + WrapBackbuffer(...) (I.6)
 + Interop surface (I.7)
```

- **Placement per ** both providers live in `Najm.Skia`. The desktop host *creates the GL context* and constructs `GpuSkiaSurfaceProvider` over it (Part IV.3); the future web host constructs the same class over a WASM-GL `GRContext`. No provider code exists outside `Najm.Skia`.
- **`CreateCompositor()`** returns a new `SkiaCompositor` (NAJM-COMPOSITOR Part II) registered as an accounting client of this provider. Compositors are per-scene and scene-lifetime; the provider is environment-lifetime and survives warm fresh-restart.
- **`budgeted: false` on every provider-created GPU surface**: the engine accounts its own surfaces; Skia's resource-cache budget is reserved for Skia-internal allocations (`saveLayer` layers, glyph atlases, path mask caches). Two budgets, two owners, zero overlap.
- **`RenderCaps` mapping:** `GpuSkiaSurfaceProvider` ⇒ `SkiaSurface | GpuBacked`; `RasterSkiaSurfaceProvider` ⇒ `SkiaSurface`; the vector writers (Part III) yield contexts carrying `SkiaSurface | VectorTarget`. A `SkiaDrawable` is therefore legal on **every** shipping configuration, vector export included (§7.6 — SkSL and filters rasterize there), and the §7.5 attach check never fires against a Skia provider.
- **Single-threaded by contract** (§3.5): no locks anywhere in provider, pool, or compositor. The provider records its creating thread in DEBUG and asserts on cross-thread use — a cheap tripwire for the classic GL-from-the-wrong-thread crash.

## I.3 `GRContext` lifecycle

- **Creation (host-side, Part IV.3):** with the GL context current on the calling thread,
 `GRContext.CreateGl(GRGlInterface.Create(name => glContext.TryGetProcAddress(name, out var p) ? p : IntPtr.Zero))`.
 The explicit Silk.NET proc loader is canonical — the parameterless `GRGlInterface.Create()` resolves through platform defaults and is the fallback, not the norm. A null interface or context is a fail-loud configuration error naming the GL version/profile requirements (Part IV.2).
- **Ownership:** the `GpuSkiaSurfaceProvider` holds the `GRContext` for the environment's lifetime (`ownsContext: true` when host-constructed). One context, one thread, one provider — the §3.5 phase contract collapses to triviality.
- **Resource cache:** `grContext.SetResourceCacheLimit(HostOptions.GpuResourceCacheBytes)` — default **256 MB**. The overlay reads `GetResourceCacheLimit()`/usage for its "Skia cache" line, distinct from provider-accounted estimated surface bytes.
- **Flush model:** the host calls `grContext.Flush(submit: true)` once per frame after `scene.Render(O)` (Part IV.5, ordering); Skia performs *implicit* flushes on render-target switches mid-frame — accepted and counted as flush economics, not fought (§4.7 note; VI.2). The compositor never flushes; `Snapshot()`/`ReadPixels` force their own syncs (the deliberate slow paths, §4.4).
- **Loss/reset:** GL device loss on desktop is rare and **does not recover** — `GRContext.IsAbandoned`-style states surface as a fail-loud exception to the driver (§2.2 discipline). Recorded, not built: abandon-and-rebuild over a fresh context (would ride the same warm-restart machinery).
- **Disposal:** strictly ordered, Part IV.12. The context disposes **while its GL context is still current**; disposing after the GL context dies requires abandon semantics and is the classic shutdown crash — the order is pinned and asserted.

## I.4 `SkiaRenderTarget`

The `IRenderTarget` realization wraps one `SKSurface`.

- `Size` and `SurfaceSpec` describe the drawable content region; a backbuffer adapter reports only the letterboxed content rectangle.
- `GetContext()` returns a reusable `SkiaDrawContext2D`, reset and stamped for the current render call.
- `Snapshot()` returns a call-scoped `SkiaImage`. The compositor never snapshots. Capture copies it immediately into a `PixelFrameLease`; `SceneNode.RenderPolicy.Always` may draw and dispose it within the parent render. Cached scene policies use explicitly owned persistent resources rather than retaining a call-scoped snapshot.
- Disposal releases provider accounting and the owned `SKSurface`; a wrapped default framebuffer never owns FBO 0.

## I.5 The pool (`SkiaSurfacePool`)

The pool realizes the provider-owned contract in `NAJM-COMPOSITOR.md`.

- **Key:** 256-pixel bucketed width/height plus normalized `SurfaceSpec`; raster sample counts normalize to one.
- **Storage:** `Dictionary<Key, Stack<Entry>>`, LIFO per key. Acquire pops or creates; release returns the surface in the same render call.
- **Epoch:** increments once per compositor render on the provider. Idle entries trim after the configured epoch horizon.
- **Capacity:** the soft cap uses estimated resident color storage, including effective sample count. Idle entries evict before allocation failure is propagated.
- **Clients:** node-backdrop scratch surfaces and vector-export raster staging. Persistent layer and accumulation surfaces are accounted but not pooled.
- **Diagnostics:** in-use/cached estimates, acquire/miss/evict/trim counts, and per-compositor attribution.

Every provider-created GPU surface is `budgeted: false`; Skia's own resource cache remains a separate budget.

## I.6 `WrapBackbuffer` — the window target adapter

`GpuSkiaSurfaceProvider.WrapBackbuffer(uint fboId, SKSizeI nativePx, SKRectI contentRect, in SurfaceSpec spec) → IRenderTarget`

- Builds `GRBackendRenderTarget(nativePx.W, nativePx.H, spec.Samples, stencilBits: 8, new GRGlFramebufferInfo(fboId, GL_RGBA8))` and wraps it via `SKSurface.Create(grContext, backendRT, GRSurfaceOrigin.BottomLeft, Rgba8888, srgbColorSpace, props)`. The window framebuffer is **plain RGBA8 with `GL_FRAMEBUFFER_SRGB` disabled** (Part IV.2): Skia writes sRGB-encoded bytes into an sRGB-tagged surface — enabling GL's encode path would double-encode. Stencil 8 is mandatory (Skia GL path rendering); the host requests it at window creation and the wrap asserts the queried bits.
- The adapter reports `Size = contentRect.Size`; `GetContext()` hands out a context whose base canvas state is `translate(contentRect.Location)` + `clipRect(contentRect)` installed **below** save-count 0 — the compositor's final 1:1 `Src` blit and FP-1's direct traversal land inside the letterbox without knowing it exists. `Snapshot()` = `surface.Snapshot(contentRect)` — capture is bar-free by construction.
- The adapter additionally exposes (backend-facing, host-consumed) the **native full-surface canvas** for the host's bar clear and overlay drawing (Part IV.5, IV.10) — these deliberately paint outside O's content clip.
- **Re-wrap on resize:** the adapter is cheap and is disposed/re-created whenever the framebuffer size or content rect changes (Part IV.7). Disposing the adapter never touches FBO 0.

## I.7 GL-texture interop (§7.5 realization)

`SkiaInterop.WrapGlTexture(GpuSkiaSurfaceProvider provider, uint textureId, SKSizeI size, GlTextureOptions opts) → IImage`

- Lowers to `new GRBackendTexture(w, h, mipmapped: false, new GRGlTextureInfo(target, textureId, internalFormat))` + `SKImage.FromTexture(grContext, tex, origin, colorType, alphaType, colorSpace)`. Defaults: `target = GL_TEXTURE_2D`, `internalFormat = GL_RGBA8`, `origin = TopLeft` (author-rendered FBO content is typically bottom-left — the author states it via `opts.Origin`; getting this wrong shows instantly as a flip), `alphaType = Premul`, `colorSpace = Srgb`.
- **Rules (constitution §7.5, made operational):** (1) the texture must come from **this provider's GL context** — supports same-context/same-thread only; share-group contexts are deferred. (2) The author completes their GL work (`glFlush`/fence) **before the frame in which the wrap is sampled** — the documented handoff. (3) The wrapped `IImage` is **borrowed**: the author owns the GL texture, must keep it alive while any drawable samples the image, and deletes it only after disposing the image and a subsequent frame flush. (4) The image obeys snapshot validity (§5.3) like any other.
- Attach-time check: a drawable holding a wrapped image validates `Caps.GpuBacked` (fail-fast on raster/offline configurations — wrap an offline path via `CopyPixels` + raster `IImage` instead; stated in the API docs).

## I.8 `IAssets` realization (`SkiaAssets`)

`SkiaAssets` loads portable asset handles and maintains native realizations in provider/environment-owned side tables:

- `FontFace → SKTypeface`
- `IPath → SKPath`
- `IImage` handles → decoded or wrapped `SKImage`
- shader descriptors → compiled Skia objects where allowed

Tables are keyed by handle identity and may coexist with other backend realizations. Creation is idempotent; native objects are disposed with the owning environment/provider, never by the portable handle. Font bytes and face index remain the reconstruction floor for HarfBuzz and future backends.

SVG assets decode through Svg.Skia into a backend cache, but exported text/math uses portable paths or `VectorPicture` commands rather than storing `SKPicture` in Core.

# Part II — `SkiaDrawContext2D`

One class, four canvases: GPU raster, CPU raster, PDF, SVG (§7.6 — "the export backend is configuration, not code"). Everything below applies to all four unless the fidelity table (Part III.5) says otherwise.

## II.1 Anatomy and state

Per context (one per target, reused across frames — I.4):

- `SKCanvas` (the target's), the target's normalized `SurfaceSpec`, read-only **`RenderScale`** (installed by the compositor/direct path/exporter per §5.1), `RenderCaps`.
- **Transform ownership:** the engine-owned node→virtual transform arrives from the traverser as a `Matrix3x2` (pinning-resolved, §6.3) and is installed via `canvas.SetMatrix(base × node)` around each drawable's `Render`; the *base* is `RenderScale` × camera mapping (× the adapter's content translate for `WrapBackbuffer`, which lives below save-count 0 and is invisible here). `Matrix3x2 → SKMatrix` is the trivial field map (row-vector convention on both sides — Appendix A of the constitution).
- **`ctx.Scale`** (§7.4) = `sqrt(|det M₂ₓ₂(canvas.TotalMatrix)|) / RenderScale` — the accumulated author→virtual scale, by definition; includes `PushTransform` contributions; computed on read (a 2×2 determinant — not worth caching).
- **`PushTransform(m)` / `PopTransform`** = `canvas.Save()` + `Concat(m)` / `Restore()`, with a per-`Render`-call balance counter (DEBUG-asserted, §7.4).
- **`PushClip(rect|IPath, FillRule)` / `PopClip`** = `Save` + `ClipRect`/`ClipPath(path, Intersect, antialias: true)` / `Restore`. **`PushOpacity(a)` / `PopOpacity`** = `SaveLayer(paint{ColorF alpha = a})` when `a < 1`, plain `Save` otherwise (§7.4); these author-tier layers are **not** counted as composition brackets (§9 counters count SPI constructs only).

## II.2 Paint stamping and descriptor caches

- **The stamping pool** (§7.4's implementation rule): a small stack of pre-allocated `SKPaint`s per context. Every Tier-1/2/2.5 call takes the top paint, `Reset()`s it, stamps the `Paint` value's fields, draws, and leaves it for the next call. Nested needs (a drawable calling helpers mid-draw) pop deeper entries; the stack grows only on unseen depth (topology event). **No `SKPaint` is ever allocated per call** — the canonical SkiaSharp leak/perf trap, closed structurally.
- **Stamping detail:** color via `paint.SetColor(new SKColorF(r,g,b,a), srgbSpace)` — our `Color` is sRGB-referenced (§3.4); tagging the set makes draws into `LinearSrgb` surfaces convert correctly. Style/stroke/cap/join/miter/AA map 1:1; `IsAntialias` defaults true (§7.4).
- **Value-keyed descriptor caches**, per context, for the objects Skia forces us to allocate: gradient/pattern `SKShader`s keyed by `Brush` value; `SKPathEffect.CreateDash` keyed by (intervals, phase); `SKColorFilter`s keyed by matrix/tint value; lowered `SKImageFilter` DAGs keyed by `EffectGraph` identity (II.7). **First appearance allocates (a topology event); steady-state repetition is a dictionary hit — zero allocation.** Caches trim on pool epochs like surfaces (I.5) so an abandoned gradient doesn't pin GPU memory forever.
- **Text runs** arrive as typesetter-cached handles (NAJM-TEXT); the context lowers them per the II.3 text-run contract — blob and glyph-path allocation lives behind transition-time builds and the epoch-trimmed text caches (the per-face **glyph-path cache** and the **mini-blob cache**, which join this section's cache family), never in the frame loop; layout allocation lives behind the typesetter's content-hash cache (§12.1), not here.

## II.3 Tiers 1 and 2

Tier-1 primitives lower directly to `SKCanvas`; Tier-2 helpers either lower to native Skia operations or expand into Tier 1 while preserving the portable contract.

### Text lowering

`DrawText` consumes portable `ITextLayout` runs. Skia keeps environment-owned caches keyed by layout/run identity:

- `FontFace → SKTypeface`
- positioned glyph run → `SKTextBlob`
- face/glyph → outline `SKPath`
- `VectorPicture → SKPicture` or replay plan

No blob, typeface, path, or picture is written into the shared layout. Static runs reuse cached blobs; dynamic numeric runs use the dedicated fixed-capacity mini-blob cache. Vector targets bypass blob drawing and emit glyph outlines or replay portable picture commands. Bitmap-only/color glyphs rasterize the smallest correct unit.

## II.4 Tier 2.5 — bulk realization

The §7.3 contract, realized without per-element managed calls; all expansion buffers are **pooled arrays sized to the largest batch seen** (growth = topology event):

- **`DrawPoints(PointBatch2D)`**
 - *Uniform paint:* `canvas.DrawPoints(SKPointMode.Points, pts, stamped{StrokeCap=Round, StrokeWidth=size})` — Skia's AA'd round-point fast path; positions copied through a pooled `SKPoint[]` view.
 - *Per-point `Colors[]` or `Scalars[]+TransferFunction`:* **`DrawAtlas`** over a cached **master disc sprite** (128 px diameter, AA'd, white) — one `SKRSXform[]` (scale = `size·deviceScale/128` per point, rotation 0) + one `SKColor[]` (colors direct, or the transfer function evaluated element-wise into the pooled color array — a tight loop over a `LookupTable`, ~10⁵ ⇒ sub-millisecond), blend `Modulate` against the white disc. Softness bound: points larger than 128 device px upsample and soften — documented; point clouds don't live there.
- **`DrawLines(LineBatch2D)`**
 - *Uniform paint:* `canvas.DrawPoints(SKPointMode.Lines, endpointPairs, stamped{StrokeWidth=width, StrokeCap})` — native AA.
 - *Per-segment colors* (the `PathRibbonNode` case): **feathered-quad `SKVertices`** — per segment, a core quad at ±w/2 plus feather strips to ±(w/2 + f) with edge alpha 0, `f = 0.5/(ctx.Scale × RenderScale)` local units (half a device pixel); premul-faded vertex colors; one `DrawVertices(vertices, Modulate, plainPaint)` for the whole batch. This is geometric AA — `DrawVertices` has no edge AA of its own — and its tolerance is pinned by a golden (Appendix C). Runtime check **SK-R08** pins the vertex-color/paint combination mode on the pinned version.
- **`DrawSprites(SpriteBatch2D)`** — `DrawAtlas(atlasImage, spriteRects, rsxforms, colors, blend, cull, paint)`: the RSXform is the (cos·s, sin·s, tx, ty) similarity per sprite — exactly the batch's pos/rot/uniform-scale contract. *Anisotropic footprints* (the §8.3 splat flush) take the vertices path instead: pooled quad expansion with the Gaussian sprite texture and per-vertex color — soft alpha edges make missing edge-AA moot.
- **Compose modes (2D):** array-order `SrcOver` default; `Additive` ⇒ per-batch `BlendMode = Plus`; `MaxPerChannel` ⇒ `Lighten` (§7.3/§8.3). Under `VectorTarget` the bulk tier routes to the portable loop (path emission — §7.3), and `Plus` degrades per the fidelity table.

## II.5 Tier 3

`SkiaDrawContext2D` additionally exposes (§7.5): the raw `SKCanvas`; a **pooled-paint escape** (`RentPaint()/ReturnPaint()` over the same stack — authors get the discipline for free); runtime SkSL (`IShader` handles, I.8, plus raw `SKRuntimeEffect` for the fearless); image filters; path effects; the full `SKBlendMode` set. **Author `saveLayer`s taken through the raw canvas are excluded from the composition-bracket counters by construction** (the counters increment only in the SPI operations). Tier-3 state discipline: authors must balance their saves within `Render` (the same DEBUG balance assert watches total save depth per call).

## II.6 The composition SPI, realized

The operational sequence the traverser drives, and each op's exact lowering:

```
PushClip(node.Clip) # II.1
[ApplyBackdrop(graph, resolvedGeometry ∩ activeClip)] # II.8 — destination-side, first
[BeginUnit(in UnitParams{hint, opacity, blend, effect})] # iff §6.7 predicate — parameters at open
 node, then children (paint order)
 [BeginMask(channel, invert) … EndMask()]
[EndUnit()]
PopClip
```

- **`BeginUnit(in UnitParams)`** → assemble the restore paint from the pooled stack: `ColorF = (0,0,0, opacity)` (alpha-only modulation), `BlendMode = blend`, `ImageFilter = Lower(effect)` (II.7; null when absent) → `canvas.SaveLayer(bounds: params.BoundsHint /* snapped visual bounds */, restorePaint)`. **`EndUnit()`** → `canvas.Restore()` — §6.7 steps 4 and 5 ride one restore (filter → alpha → blend is Skia's paint pipeline order, matching the pipeline exactly). Unit counter++ at Begin; nesting depth tracked here.
- **`BeginMask(channel, invert)`** → nested `canvas.SaveLayer(restorePaint{ BlendMode = DstIn, ColorFilter = MaskFilter(channel, invert) })`; mask children then draw normally (SrcOver among themselves, their own paint order — §6.7); **`EndMask()`** → `Restore()`, which multiplies the open unit — the classic Skia idiom, zero scratch (COMPOSITOR §5). `MaskFilter`:

 | channel, invert | `SKColorFilter` |
 |---|---|
 | `Alpha`, false | none |
 | `Alpha`, true | color matrix, alpha row `a′ = 1 − a` (RGB rows zero — `DstIn` reads alpha only, so premul color rows are irrelevant) |
 | `Luminance`, false | `SKColorFilter.CreateLumaColor()` (Skia's SVG-exact luma→alpha; on premul input this yields lum·α — luminance modulated by coverage, the intended semantics) |
 | `Luminance`, true | **one** color matrix, alpha row `a′ = 1 − (0.2126R + 0.7152G + 0.0722B)` on premul values (algebraically = 1 − lum·α) — the compose-free realization of "invert after extraction" |

- **Leaf `Effect` (FP-2):** no layer — the lowered graph stamps onto the node's own paint `ImageFilter` for its draws. Byte-parity with the bracketed form for single-primitive leaves is part of the no-op-equivalence family (§18).
- **Bracket spec inheritance:** Skia's `saveLayer` inherits the surface's color type/space/sample count internally; the MSAA no-op-equivalence row (check **SK-R05**) is the proof obligation, not a code path.

## II.7 `EffectGraph` lowering

`Lower(graph) → SKImageFilter`, cached by graph identity/value (II.2):

| Descriptor | `SKImageFilters` construction |
|---|---|
| `Source` | `null` input (Skia convention: null = source) |
| `Blur(σ)` | `CreateBlur(σ, σ, SKShaderTileMode.Decal, input)` — **decal is Najm's normative edge policy**, and the tile-mode overload exists (Appendix A) |
| `Offset(dx,dy)` | `CreateOffset(dx, dy, input)` |
| `ColorMatrix` / `Tint` | `CreateColorFilter(matrixFilter, input)` |
| `Dilate(r)` / `Erode(r)` | `CreateDilate/CreateErode(r, r, input)` |
| `DropShadow(dx,dy,σ,color)` | `CreateDropShadow(dx, dy, σ, σ, color, input)` |
| `Merge(a,b,…)` | `CreateMerge(filters)` — painted in order (§6.7) |
| `Compose(outer ∘ inner)` | `CreateCompose(outer, inner)` |

- **Units and pinning, for free:** Skia image filters are CTM-affected — parameters written in local units scale with the accumulated transform at draw, which is exactly §6.7's rule; a pinned label's glow pins with it because the pinned CTM is what's installed. One filter instance serves every transform — the cache is CTM-independent by construction.
- **Layer-tier `Effect`** (applied during the merge `DrawSurface`, COMPOSITOR §2c) executes under an identity-plus-offset CTM, so lowering **pre-scales parameters by `RenderScale`** — realizing the architecture's rule that layer-effect parameters are virtual units. The layer-effect cache is keyed by (graph, RenderScale) accordingly.
- **The bounds transform** is a Core function over the same descriptors (blur outsets 3σ, etc.); this document *consumes* it in II.8 and Part III and does not restate it.

## II.8 Node-tier `Backdrop` (per NAJM-COMPOSITOR Part II §4)

**The construct (normative):** with `PushClip` already active and region `R = resolved subtree geometry ∩ active clip` device-resolved by the traverser:

1. `outset = BoundsTransform(graph)(R)`, intersected with the destination surface bounds after step 2's padding logic.
2. **Acquire** scratch from the pool at `outset` size, destination's normalized spec.
3. **Copy:** `scratch.Canvas.DrawSurface(dest, −outset.Location, Src)` — read-while-write, no snapshot (COMPOSITOR §8). Where `outset` exceeded the destination surface, **clamp-pad**: stretch the nearest copied edge pixels into each margin and fill all four corner regions from the nearest copied corner pixel. This is expressed as four edge draws plus four corner fills, realizing Najm's edge-clamp policy.
4. **Write-back:** on `dest.Canvas` — `Save`; `ClipRect(R_device)` (the `Clip` path is already active, so the effective region is the shaped `R` — preserving the architecture's shaped-region semantics); `DrawSurface(scratch, outset.Location, paint{ ImageFilter = Lower(graph), BlendMode = Src })`; `Restore`.
5. **Release** the scratch (same frame). Backdrop-construct counter++.

Sample-beyond ✓ (the outset copy), write-only-region ✓ (the clip + `Src`), opacity/blend independence ✓ (the unit has not opened yet), replacement-precedes-composite ✓. **The rec-based backdrop-`saveLayer` variant** (`SaveLayer(rec{Backdrop, Paint:{Src}})` under a region clip) is legal **only for pointwise graphs** (identity bounds transform), where the two constructs are pixel-identical; it is gated on runtime checks **SK-R01** (Src-restore = replace on semi-transparent destinations), **SK-R02** (opacity independence), **SK-R03** (edge behavior) and exists as an optimization, not a requirement.

## II.9 What the context does *not* do

No composition decisions (the traverser owns predicates, ordering, and regions), no target lifecycle (the compositor's), no color policy (the spec's tags do the work), no y-flips (Skia origins), no snapshots (the CoW rule), and no text shaping or layout (the typesetter owns both; the context draws cached layout handles per II.3 and never measures a glyph). The context is a lowering layer; every judgment above it has a named owner.

---

# Part III — Vector export and the direct path

## III.1 Writers and page geometry

Core's `IVectorTargetWriter` is realized twice:

- **`PdfWriter(path, VectorExportOptions)`** — `SKDocument.CreatePdf(stream, metadata)`; `Begin(size)` = `BeginPage(size.W × Scale, size.H × Scale)` → the page canvas; `End()` = `EndPage` + `Close`.
- **`SvgWriter(path, VectorExportOptions)`** — `SKSvgCanvas.Create(SKRect.Create(size × Scale), stream)`; `End()` disposes the canvas (which finalizes the XML).

**`VectorExportOptions` — two knobs, deliberately distinct:**

| Knob | Meaning | Default |
|---|---|---|
| `Scale` | page geometry: **points per virtual unit** (PDF pt; SVG user units). This is the `RenderScale` the scene observes (§5.1). | 1 — a 1920×1080-virtual scene is a 1920×1080 pt page (≈ 26.7 × 15 in). Publication figures typically pass `Scale: 0.25` (≈ 6.7 in wide) or an explicit size. |
| `RasterScale` | **pixels per virtual unit** for every raster embed this Part produces (`VectorPolicy.Raster`, RB/Backdrop pipelines, unexpressible-construct rasterization). | 2 (⇒ 144 dpi at `Scale = 1`; dpi = 72·RasterScale/Scale). |

`SKDocumentPdfMetadata.RasterDpi = 72 × RasterScale / Scale` so Skia's *own* internal rasterizations (of constructs it decides to rasterize) match ours in density. Both writers hand the direct path a `SkiaDrawContext2D` with `Caps = SkiaSurface | VectorTarget` and `RenderScale = Scale`.

## III.2 The direct path over a vector canvas

Core's direct path (COMPOSITOR I §1.3) walks layers into the writer's context: per visible layer — viewport clip if set; `PushOpacity(layer.Opacity)`; the **per-layer isolation bracket** (a `SaveLayer`, skipped under FP-6's registry-counted predicate) carrying `Layer.Blend` where the format expresses it; the shared traverser inside. What the format cannot express falls to III.3/III.4, per the fidelity table — **never a silent `SrcOver` downgrade**.

## III.3 The raster-embed pipelines (realized)

Both pipelines are **Core logic** (direct path / traverser — they own walking); this backend supplies only raster targets (via `env.Surfaces` — a raster provider is part of every export environment, Part V.1) and the image draws. Both are licensed by render idempotence (§4.1): re-walking is re-rendering, and re-rendering is free of observable effect by contract.

- **`ReadsBackdrop` layer:** at the RB boundary, the direct path (a) re-renders every already-emitted layer into a pooled raster target `B` at `RasterScale` (a second walk of the below-stack — vector output above is untouched); (b) initializes a raster target `L` from `B` (replace-draw, color-converted); (c) renders the RB layer's tree into `L` via a raster context at the same scale; (d) lerp-merges `L` over `B` per the closed form (COMPOSITOR §6 — the same two-pass); (e) draws `B`'s **region** (the layer's viewport rect, else full canvas) into the vector canvas as an image, `Src`, 1:1 in page units. Layers above continue as vector on top. Cost: one extra below-stack render per RB layer per export — offline territory, accepted.
- **Node-tier `Backdrop`:** the traverser, on `VectorTarget` ∧ `Backdrop ≠ null`, (a) re-walks the **owning layer** from its root, stopping before the unit, into a pooled raster target clipped to `R = resolved subtree geometry ∩ active clip` outset by the graph's bounds transform (same clamp-pad rule as II.8); (b) draws that raster into the vector canvas through `paint{ImageFilter = Lower(graph), BlendMode = Src}` clipped to `R` — the region embeds as a filtered image (Skia's vector canvases rasterize filtered image draws into the embed, check **SK-R09**); (c) the unit then composites as vector above it. Nested in-layer backdrops re-walk per occurrence — quadratic and accepted for figure workloads.

## III.4 `VectorPolicy.Raster` machinery (§7.6)

Resolution precedence (instance → `[VectorExport]` class attribute → `Auto`) is Core's; on `Raster`, the traverser renders the node's subtree (or the batch) into a pooled raster target sized to its device bounds at `RasterScale` — via a raster context, same traverser, composition included — and emits **one** `DrawImage` into the vector canvas. This is also the normative fallback for every "rasterize the affected unit" row below: the same machinery, engine-invoked.

## III.5 The fidelity table

Raster (GPU/CPU) is the reference semantics; the table states each feature's vector-format realization, its runtime check where Skia's emission is the uncertainty, and the **normative fallback** (always: correct raster embed via III.4 — never a wrong vector).

| Feature | PDF (`SKDocument`) | SVG (`SKSvgCanvas`) | Check / fallback |
|---|---|---|---|
| Tier-1 paths + `FillRule` | native (even-odd native) | native (`fill-rule`) | — |
| Strokes: width/cap/join/miter/dash | native | native | — |
| Gradients (linear/radial), patterns | native, sRGB (§3.4) | native, sRGB | — |
| Text (outline-capable faces) | **glyph outlines** via the glyph-path cache (II.3) | same | /default; no font dependency; `VectorTextPolicy.Embed` deferred (rides `VectorExportOptions`) |
| Text (bitmap/COLR/emoji glyphs) | **rasterized unit** (III.4) | same | no outlines exist; **SK-R14** flags the face — never a silent drop |
| Text on path | transformed outline paths | same | inherits the two rows above per face; placements bake into path transforms |
| Images | embedded (Flate/DCT) | base64-embedded | — |
| `Clip` (path, fill rule) | native | native | — |
| Group opacity (unit bracket, `PushOpacity`) | transparency group, `/ca` | group `opacity` | **SK-R10** confirms emission; fallback rasterize unit |
| 12 separable blends (§7.4) | native PDF blend modes | `mix-blend-mode` where the Skia SVG backend emits it | **SK-R11** per mode per format; unexpressed ⇒ **rasterize the blended unit** |
| `Plus` (additive) | **no PDF equivalent** ⇒ rasterize unit | rasterize unit (portable subset marks it raster-only, §7.4) | — |
| `Mask` slot (channel/invert) | PDF soft mask (SMask) where emitted | SVG `<mask>` where emitted | **SK-R12**; fallback rasterize the masked unit |
| Unit / layer `Effect` graphs | Skia rasterizes filtered groups into the output automatically | same | **SK-R09** pins density = `RasterDpi`; `VectorPolicy.Raster` is the author's explicit control |
| Node `Backdrop` | III.3 raster-embed pipeline (ours) | same | golden vp-09 (Appendix C) |
| `ReadsBackdrop` layer | III.3 pipeline (ours) | same | golden vp-10 (Appendix C) |
| Layer `Blend` at the direct-path bracket | as blends row | as blends row | unexpressed ⇒ rasterize the layer walk |
| Bulk tier (points/lines/sprites) | portable loop ⇒ per-element vector paths (§7.3); `DrawAtlas`/`DrawVertices` never run on vector | same | dense batches ⇒ `VectorPolicy.Raster` (the §7.6 story); file-size warning in docs |
| 3D projection flush | inherits all rows (it emits through Tiers 1–2.5) — vector 3D per §8.3 | same | `Additive`/`MaxPerChannel` per the blends rows (§8.3/§7.6) |
| SkSL / Tier-3 filters / full blends | rasterize (§7.6) | rasterize | — |
| `SvgNode` (`SKPicture` assets) | replays as vector | replays as vector | — |

**Reading rule:** a scene using only the "native" rows exports as pure vector on both formats; every other row degrades to a *correct raster region* at `RasterScale`, and the §18 `VectorPolicy` structural check (extended in App. C) counts embedded images so degradations are visible in review, not discovered in print. **Text notes:** path/rule-only `VectorPictureRun` content replays vector-natively; a run containing an image command is reported and embedded as raster. `RuleRun`s are native rects everywhere. The fidelity table is the one place authors look to predict an export; text's honest exception (outline-less glyphs) is a row, not a surprise.

---

# Part IV — `Najm.Host.Desktop` — the GL host

## IV.1 Windowing and GL stack

**Silk.NET** supplies windowing, input, and OpenGL through one family. Its `IView` manual pump (`Initialize` / `DoEvents` / `SwapBuffers`, verified in Appendix A) lets the host own the single frame loop from architecture §4.7; its proc-address loader plugs directly into `GRGlInterface.Create`; and the same OpenGL layer can serve `Najm.GL3D` without introducing another platform stack.

**Control shape:** the host uses `window.Initialize()` then its **own** `while (!window.IsClosing)` loop with `DoEvents()` and explicit `SwapBuffers()` (`ShouldSwapAutomatically = false`), never `window.Run()`'s split Update/Render callbacks — §4.7 is one loop, so the host is one loop.

## IV.2 Window and GL pins

- **API:** OpenGL **3.3 core profile** (Skia's comfortable floor; universal on the Victus-15 class and anything newer). ANGLE (GLES-over-D3D) is the deferred fallback for native-GL driver failures on Windows — `GRGlInterface.CreateGles` + ANGLE EGL, a provider-construction swap, zero engine changes.
- **Framebuffer:** RGBA8, **stencil 8** (mandatory — Skia GL path rendering; asserted after creation), **depth 0** (2D engine; Skia needs none), `Samples = HostOptions.Msaa` (**default 1** — Skia's analytic AA is the default quality path; MSAA is the opt-in that buys edge AA for `DrawVertices` content). **`GL_FRAMEBUFFER_SRGB` stays disabled** and the framebuffer is *not* an sRGB GL format: Skia writes already-encoded sRGB into the sRGB-*tagged* wrap (I.6); enabling GL's encode would double-encode.
- **VSync:** on by default (`window.VSync = true`, swap interval 1); `ClockPolicy` remains the time authority (§2.1) — vsync paces presentation, the clamped clock paces simulation.
- **The main-thread rule:** `Run` blocks the calling thread, which must be the process main thread (GLFW/macOS event-pump strictness). All GL, Skia, and scene code lives there (§3.5).

## IV.3 Bootstrap (the composition root, §4.6)

```
Run(Func<Scene> make): # factory per window = Create(WindowOptions per IV.2); window.Initialize() # context current here
 input = window.CreateInput() # keyboards, mice
 gr = GRContext.CreateGl(GRGlInterface.Create(silkLoader)) # I.3
 gr.SetResourceCacheLimit(opts.GpuResourceCacheBytes) # provider = new GpuSkiaSurfaceProvider(gr, ownsContext: true, opts.Pool)
 env = new SceneEnvironment(assets: new SkiaAssets(provider),
 typesetter: opts.Typesetter ?? NullTypesetter.Instance, # App injects the real one
 audio: opts.Audio ?? NullAudioSink.Instance, # surfaces: provider,
 caps: SkiaSurface | GpuBacked)
 scene = make(); scene.Load(env) # Load binds the environment and acquires the compositor
 … main loop (IV.5) … shutdown (IV.12)
```

## IV.4 Letterbox math (normative)

Definitions: framebuffer `fb = (Wpx, Hpx)` (pixels, from `FramebufferSize`); window client `win = (Ww, Wh)` (screen units, from `Size`); virtual `V = (vw, vh)` (§5.1).

```
s = min(Wpx/vw, Hpx/vh) # RenderScale — hi-DPI falls out via fb
content = (ceil(vw·s), ceil(vh·s)) # ≡ A's allocation rule; provably ≤ fb per axis
offset = (⌊(Wpx−cw)/2⌋, ⌊(Hpx−ch)/2⌋)
O = provider.WrapBackbuffer(0, fb, rect(offset, content), spec(Srgb, opts.Msaa))
```

**Input, inverse:** pointer `p` arrives in window units → `q = p ⊙ (fb ⊘ win)` per axis (the DPI ratio) → `virtual = (q − offset) / s`. **Unclamped** — coordinates outside `[0, V]` flow through; the letterbox round-trip property test (Appendix C) pins `map⁻¹(map(x)) = x` within ε across DPI ratios.

## IV.5 The frame loop (realizing §4.7)

```
while !window.IsClosing:
 window.DoEvents() # SYNC begins
 if restartRequested: WarmRestart() # IV.9
 if fbResized: recompute IV.4; rewrap O # IV.7
 inputBlock = drain event queue → translate → inverse-letterbox # IV.6
 time = clock.Advance() # ClockPolicy; clamp if Live
 publish ambients (future IPC — §4.6)
 scene.Tick(new TickContext(time, inputBlock))
 if letterboxed: nativeCanvas.Clear(opts.BarColor) # bars only — outside O
 scene.Render(O) # compositor path; FP-1 rides O directly
 gr.Flush(submit: true)
 if capture: O.Snapshot() → CopyPixels into pooled PixelFrameLease → sink.Submit(frame, lease)
 if overlayVisible: DrawOverlay(nativeCanvas); gr.Flush(submit: true) # excluded from capture
 window.SwapBuffers()
```

The present order is: scene render, GPU flush, capture, overlay, final flush, swap.

## IV.6 The input pump

Silk.NET events are handler-buffered into a host queue during `DoEvents`, then drained into the pooled `InputBlock` (§9.1):

| Silk.NET | InputBlock |
|---|---|
| `IMouse.MouseMove` | pointer move, id 0, coords per IV.4 |
| `MouseDown/Up` | pointer down/up (+ button map) |
| `Scroll` | scroll event (wheel deltas) |
| `IKeyboard.KeyDown/Up` | key down (repeat-flagged where the platform says so) / key up |
| `IKeyboard.KeyChar` | **text input (rune)** — §9.1's required channel |

- **Snapshots are host-maintained** from the same events (pointer position/buttons, key states) — one source of truth, no per-frame device polling, deterministic ordering into the block.
- **Host-reserved keys**: the overlay toggle (default `F1`) and manual restart (default `F5`) are consumed at drain and never enter the block.
- Pointer id is 0 (mouse) — the §9.1 field exists for web/touch futures; desktop emits one pointer.
- Deterministic drives never come through here (§2.1) — `OfflineRenderer` supplies the canonical empty block; this pump is live-mode machinery only.

## IV.7 Resize and DPI

`FramebufferResize` sets a flag; the loop top recomputes IV.4, disposes and re-wraps `O` (adapter only — FBO 0 is untouched), and the compositor re-acquires per its §5.3 triggers on the next `Render`; superseded surfaces release immediately and pool residue trims by epoch. DPI changes manifest as `fb/win` ratio changes and ride the same path. **Known platform limitation:** on Windows, GLFW's modal move/size loop blocks `DoEvents` during the drag — frames stall until release. Rendering from inside the resize callback is the known workaround; **not built** (§1.3/§5.1 deprioritize window resizing), noted so nobody rediscovers it as a bug.

## IV.8 Capture

Capture occurs after the scene render and GPU flush, before overlay and swap. The content-rect adapter snapshots only the letterboxed content. The host immediately copies pixels into a pooled `PixelFrameLease` and transfers ownership to `IFrameSink.Submit`; no `SKImage` or surface snapshot is retained by a sink.

A synchronous sink disposes the lease before returning. An asynchronous sink owns a bounded queue and disposes after encode/write; when full, the host applies configured backpressure or drops the frame and increments diagnostics. This boundary avoids copy-on-write retention of the backbuffer and makes cross-thread ownership explicit.

## IV.9 Hot reload

`dotnet watch` applies supported ordinary method-body deltas in place. Manual warm restart, default `F5`, is the reliable fallback: dispose the current scene, re-invoke the stored factory, and load it into the retained environment/provider.

Automatic classification of iterator/state-machine edits is not required for the initial host. It may be added only after runtime-spike tests demonstrate reliable behavior for the pinned .NET version. Type-shape edits that the runtime refuses still fall back to the ordinary process restart performed by `dotnet watch`.

## IV.10 The debug overlay (§15)

Drawn by the host into the **native full-surface canvas** (I.6) after capture — top-right panel, plain `SkiaDrawContext2D` drawing, no scene involvement, no InputBlock traffic. Sources: `CompositorStats` (brackets itemized, nesting peak, RB barriers, target/A bytes — COMPOSITOR §9), pool stats (I.5), provider bytes vs. `GRContext` cache usage (**two lines**), FPS/frame-time ring, clock mode, coroutine listing (Core debug hooks, §15), **GC-collections canary** (§3.6), capture drop counter (IV.8), current `RenderScale`.

## IV.11 `HostOptions` (consolidated)

| Option | Default | Ref |
|---|---|---|
| `Clock` | — (required) | §2.1 |
| `Window` (size, title, resizable) | 1280×720, resizable | §4.6 |
| `Msaa` | 1 | IV.2, |
| `VSync` | true | IV.2 |
| `BarColor` | opaque black | |
| `Typesetter`, `Audio` | Core null objects | |
| `Capture` | off | §4.6, IV.8 |
| `GpuResourceCacheBytes` | 256 MB | |
| `Pool` (soft cap, trim horizon) | COMPOSITOR §7 defaults | I.5 |
| `OverlayKey` / `RestartKey` | `F1` / `F5` | |
| `FfmpegPath` | `PATH` discovery | Part V.4 |

## IV.12 Shutdown and dispose order (pinned)

`scene.Stop() → scene.Unload()` (compositor + its targets dispose here) → capture sink `End()` → dispose `O` adapter → `provider.Dispose()` (drains pool; asserts zero outstanding client surfaces) → `gr.Dispose()` **with the GL context still current** → `input.Dispose()` → `window.Dispose()`. Injected capabilities (`Typesetter`, `Audio`) are disposed by their injector unless `HostOptions.OwnsInjected` — the host never destroys what it didn't create. This exact order is a shutdown test (Appendix C); violating this order risks GL teardown faults.

---

# Part V — Offline drivers, environments, media sinks

## V.1 Environment assembly matrix (realized)

| Configuration | `Assets` | `Typesetter` / `Audio` | `Surfaces` | `Caps` | InputBlock |
|---|---|---|---|---|---|
| **DesktopHost** (IV.3) | `SkiaAssets(gpuProvider)` | injected via `HostOptions` (Core null objects default) | `GpuSkiaSurfaceProvider` | `SkiaSurface \| GpuBacked` | live pump (IV.6) |
| **Offline** (`SkiaOffline.Render`) | `SkiaAssets(rasterProvider)` | injected params (null objects default; `Audio` typically `CueRecorder` — §11) | `RasterSkiaSurfaceProvider` | `SkiaSurface` | canonical empty block (§2.1) |
| **Vector export** (`SkiaExport.Pdf/Svg`) | `SkiaAssets(rasterProvider)` | same as offline | `RasterSkiaSurfaceProvider` — **staging for III.3/III.4** | writer context: `SkiaSurface \| VectorTarget`; staging contexts: `SkiaSurface` | none (single `at:` evaluation, §4.6) |

Notes: the raster provider in the vector row is not optional — the pipelines and `VectorPolicy.Raster` require it even for a "pure vector" figure (it simply goes unused when nothing degrades). Every row satisfies the closed five-capability environment (§4.2); nothing here adds a slot.

## V.2 The `Najm.Skia` conveniences

Indicative shapes; each assembles the matrix row above and delegates to the Core loop:

```csharp
SkiaOffline.Render(Func<Scene> make, OfflineOptions o) // fps, duration|frames, scale, sink, typesetter?, audio?
 → OfflineRenderer.Render(make(), env, o.Fps, o.Duration, o.Scale, o.Sink)

SkiaExport.Pdf(Func<Scene> make, string path, double at, VectorExportOptions v = default, …)
 → VectorExporter.Export(make(), env, at, new PdfWriter(path, v))
SkiaExport.Svg(…) // same over SvgWriter
```

Factories, not instances, for symmetry with (and because export evaluates a fresh instance at `at:` per §2.2's fresh-instance semantics). The constitution's §4.6 one-liners are these methods.

## V.3 `FrameSink.PngSequence`

Consumes an owned `PixelFrameLease`, encodes a PNG, and disposes the lease. Offline rendering may perform this synchronously; optional asynchronous operation uses a bounded queue.

## V.4 `FrameSink.FfmpegPipe`

Spawns ffmpeg and writes the lease's raw pixel rows to stdin in the declared format/stride, then disposes the lease. Startup validates executable discovery and stream parameters. Broken pipes and non-zero exit codes fail loudly with stderr context. A bounded queue provides optional live-capture decoupling; queue overflow follows the configured drop/backpressure policy.

# Part VI — Threading, flush economics, failure, performance

## VI.1 Thread model

Everything runs on the process main thread — pump, tick, render, GL, Skia, sinks' `Submit` — per §3.5, with the DEBUG tripwires of I.2. **The single deliberate exception in the entire system is the ffmpeg stderr drain thread (V.4):** pure pipe I/O, zero engine state, joined at `End`. The §3.5 phase contract needs no locks anywhere; it is enforced by construction and asserted in DEBUG.

## VI.2 Flush economics

Per COMPOSITOR II §2, the default orders a run's renders before its merges (fewest GL target switches); RB barriers serialize by contract. Bind counts, stated so profiles have a baseline:

- **RB-free, N layers:** N layer renders + A (all merges) + O (blit) ≈ **N + 2** binds; FP-1 frames are exactly **1**.
- **S1's world + RB-lens + HUD** (App. B): runs `[][RB ][]` degenerate to one layer each, so binds interleave — `A, (init+render), A(lerp), A, O` ≈ **7**, plus the backdrop scratch pair when a node-`Backdrop` fires (bounded, pooled).
- Each bind boundary is an implicit Skia flush (I.3); the host's explicit `Flush(submit: true)` per frame is the only submission point. Mid-frame target switches are visible through frame-time diagnostics; add a dedicated flush counter only if profiling requires it.

## VI.3 Failure modes (consolidated; all fail-loud per §2.2)

| Failure | Surface point | Behavior |
|---|---|---|
| Surface/target allocation | provider `CreateSurface` / pool create | exception to the driver (COMPOSITOR §9); pool soft-cap evicts first |
| SkSL compile error | `IAssets.LoadShader` (I.8) | at **load**, with Skia's error text — never at draw |
| GL context loss | any GR call (I.3) | not recovered initially; exception names the condition; rebuilding over a fresh context remains future work |
| ffmpeg missing / dies / nonzero exit | sink `Begin` / `Submit` / `End` (V.4) | fail loud with stderr tail |
| Odd dimensions under `yuv420p` | sink `Begin` (V.4) | assert with the fix named |
| Stencil bits ≠ 8 on the window | wrap assert (I.6) | configuration error naming IV.2's pins |
| Cross-thread engine call | DEBUG asserts (I.2, VI.1) | assert with the offending thread |

## VI.4 Performance notes

- **Readback:** ≈ 8.3 MB/frame at 1080p (IV.8); capture is the deliberate slow path; PBO async readback is a possible measured optimization.
- **Snapshot discipline:** capture and `RenderPolicy.Always` dispose transient snapshots within the same operation. `WhenInvalidated`/`Once` do not retain such snapshots; they use an explicitly owned cache resource whose updates are controlled and accounted. No writable surface enters its next frame with an accidental live snapshot.
- **Atlas softness bound:** per-color points soften above 128 device px (II.4) — by design; not a point-cloud regime.
- **`DrawVertices` has no edge AA** (II.4): feathered quads approximate it; `HostOptions.Msaa = 4` is the quality lever for vertex-heavy scenes (IV.2), at bandwidth cost.
- **Descriptor caches:** first appearance of a brush/dash/filter value allocates (topology); a scene animating *brush values* per frame churns small objects by construction — the documented answer is animating uniforms on an `IShader` (I.8) instead.

---

## Appendix A — Binding-check registry

**A.1 Confirmed at the API level.** Re-verify this registry against the pinned SkiaSharp and Silk.NET versions on every package bump:

`SKCanvas.SaveLayer(in SKCanvasSaveLayerRec { Bounds, Paint, Backdrop, Flags })` · `SKCanvas.DrawSurface(surface, x, y[, sampling][, paint])` · `DrawAtlas` full overload set (sprites, RSXforms, colors, blend, cull, sampling, paint) · `DrawPoints(SKPointMode, …)` · `SKBlender` (+`CreateBlendMode`/`CreateArithmetic`), `SKPaint.Blender`, `SKRuntimeEffect.CreateBlender`/`ToBlender` · `SKImageFilter.CreateBlur(σx, σy, SKShaderTileMode[, input][, cropRect])` · `SKColorFilter.CreateLumaColor()`, `CreateColorMatrix(span)` · `GRGlInterface.Create(GRGlGetProcedureAddressDelegate)` + `CreateOpenGl`/`CreateGles` · `GRContext.CreateGl(iface[, options])`, `Flush(submit, synchronous)`, `Submit(synchronous)`, `Get/SetResourceCacheLimit` · `GRBackendRenderTarget(w, h, samples, stencilBits, GRGlFramebufferInfo)` · `SKSurface.Create(SKImageInfo…)` raster family and `Create(GRRecordingContext, budgeted, info, sampleCount, origin[, props])` GPU family · `SKImage.FromTexture(context, GRBackendTexture, origin, colorType, alphaType, colorSpace)` · `SKDocument.CreatePdf(path|stream[, metadata])`, `DefaultRasterDpi = 72` · Silk.NET `IView`: `Initialize`, `DoEvents`, `DoUpdate`, `DoRender`, `IsClosing`, `FramebufferSize`, `FramebufferResize`.

**Text APIs:** `SKTextBlobBuilder` with `AllocateRun` / `AllocatePositionedRun` / `AllocateRotationScaleRun` and the corresponding run buffers · `SKFont.GetGlyphPath` / `GetGlyphPaths` · `SKFont` hinting/edging/subpixel setters and `Metrics` · `SKPathMeasure` (`GetPosition`, `GetTangent`, `Length`) · `SKCanvas.DrawText(SKTextBlob, x, y, SKPaint)`. `AllocatePositionedRun` creates a managed buffer wrapper on the pinned binding, so blob construction is confined to content transitions. Najm.Text shapes through HarfBuzzSharp directly; `SkiaSharp.HarfBuzz.SKShaper` is not part of the pipeline because itemization and caching live above shaping.

**A.2 Runtime checks** (behavioral; executed as tests once per pinned version; each names its consumer and fallback):

| Id | Question | Consumer | Fallback if failed |
|---|---|---|---|
| SK-R01 | Rec-based backdrop `Src`-restore = replace (not over) on semi-transparent destinations | II.8 optional variant | variant stays off; scratch construct (primary) unaffected |
| SK-R02 | Rec-based backdrop is independent of subsequent unit opacity | II.8 optional variant | same |
| SK-R03 | Backdrop snapshot edge behavior at region/surface edges | II.8 optional variant | same |
| SK-R04 | One-pass `SKBlender` lerp ≡ two-pass golden (incl. transparent below) | COMPOSITOR §5 alternative | two-pass stays normative (it already is) |
| SK-R05 | `saveLayer` inherits enclosing sample count (MSAA-4 no-op equivalence) | FP-1 / II.6 | document divergence; force canonical path under MSAA until resolved |
| SK-R06 | `Snapshot(SKRectI)` overload present | capture crop (IV.8) | `Snapshot().Subset(rect)` |
| SK-R07 | GPU pre-upload spelling (`ToTextureImage`-family) | asset load (I.8) | lazy first-draw upload (benign memoization) |
| SK-R08 | `DrawVertices` vertex-color × paint combine under `Modulate` with shaderless paint | feathered lines, splats (II.4) | adjust mode (`Dst`) per observed semantics; golden re-pins |
| SK-R09 | Vector canvases rasterize filtered image draws at `RasterDpi` density | III.3/III.5 effect rows | pre-rasterize via III.4 before the draw (density then ours by construction) |
| SK-R10 | Group opacity emitted natively (PDF `/ca` group; SVG `opacity`) | III.5 | rasterize the unit (III.4) |
| SK-R11 | Each of the 12 separable blends emitted per format | III.5 | rasterize the blended unit |
| SK-R12 | Mask constructs emitted (PDF SMask; SVG `<mask>`) | III.5 | rasterize the masked unit |
| SK-R13 | **RSXform parity** — first-use golden: a rotated-glyph blob via `AllocateRotationScaleRun` matches per-glyph `Save`/`Concat`/`DrawText` composition within AA tolerance | on-path text (II.3) | on-path draws fall back to per-atom transformed draws (correct, slower); warning once |
| SK-R14 | **Glyph-path availability** — per face at first vector use: `GetGlyphPath` on a sample glyph yields an outline | vector text route (II.3, III.5) | bitmap/COLR/variable-quirk faces are flagged; their runs take the architecture's raster fallback on vector targets; log names the face |
| SK-R15 | **No blobs on vector** — debug assert in the `DrawText` lowering: the blob branch is unreachable when the canvas is a `VectorTarget` | vector text route (II.3) | documents the route a future `VectorTextPolicy.Embed` would legalize |

## Appendix B — Worked scenarios

Each: setup → trace → invariants checked. These are executable integration-test scenarios for the current contracts.

**S1 — Three-layer live frame (world + RB lens + HUD).** 1920×1080 virtual on a 2560×1440 window: `s = min(2560/1920, 1440/1080) = 4/3`; content `(⌈2560⌉, ⌈1440⌉) = (2560, 1440)` — exact fit, offset (0,0), no bars, no bar clear. Runs partition `[world][RB lens][HUD]`. Trace: pump → InputBlock → Tick → Render: bind world target, traverse, merge into A; bind lens target, **init = replace-draw of A** (tag-converted), traverse lens tree, **lerp-merge** into A (two-pass `DstIn`+`Plus`); bind HUD, traverse — a frosted tooltip inside HUD fires the **node-`Backdrop` scratch construct** (acquire outset scratch → copy+clamp-pad → filtered `Src` write-back under the panel's clip → release); merge HUD into A; blit A → O. Binds ≈ 7 (VI.2) + the scratch pair. Invariants: **second identical frame performs zero allocations and zero pool creates** (the scratch acquire hits the pooled entry — acquire/release events, no miss); zero snapshots anywhere (CoW rule); counters read 1 backdrop construct, RB barriers = 1; frost confined to `resolved subtree geometry ∩ active clip` and unfaded by tooltip opacity (goldens). This scenario demonstrates why, with the panel near content edges, the blur must sample beyond the region while writing only inside it — jointly inexpressible in the rec construct.

**S2 — Resize/DPI storm.** Drag the window from a 1080p monitor onto a 4K 200%-scale monitor. Per `FramebufferResize` burst: loop-top recompute (IV.4 — `fb/win` ratio jumps to 2, `s` recomputes), dispose + re-wrap the O adapter (cheap; FBO 0 untouched), compositor re-acquires layer targets and A at the new `ceil(V×s)` on next render, superseded surfaces release immediately, old pool buckets trim after 120 epochs. Invariants: every allocation during the storm is a **permitted topology transition** — the GC canary may tick during the storm and must go silent once settled; input round-trip continuity holds through the ratio change (the property test's DPI rows); no frame renders with mismatched `s`/adapter (both recomputed at the same loop point, before InputBlock).

**S3 — Warm restart walkthrough.** A change to coroutine suspension structure or scene shape cannot safely update active state. The author presses the host restart command: the loop calls `scene.Stop()` and `scene.Unload()`, re-invokes the stored factory, and `Load`s the fresh scene over the retained environment. The `GRContext`, provider and warm pool, decoded assets, and typesetter caches remain alive. Ordinary supported `Render`/`Update` body edits still apply in place. Type-shape edits refused by the runtime fall back to `dotnet watch` process restart.

**S4 — Capture session (live, ffmpeg).** Per frame: render → GPU flush → content-rect snapshot → immediate `CopyPixels` into a pooled `PixelFrameLease` → transfer the lease to the sink → draw overlay → swap. A bounded asynchronous sink queue owns leases until written and disposes them afterward; overflow follows the configured drop/backpressure policy and increments diagnostics. Recordings are pre-swap, bar-free, and overlay-free, and no `SKImage` crosses the submission boundary.

**S5 — PDF export: the constitution's C.3 banner + an invert-luminance lens RB layer.** `SkiaExport.Pdf(() => new BannerFigure(), "fig3.pdf", at: 2.0, new(Scale: 0.25, RasterScale: 4))` → 480×270 pt page, embeds at 288 dpi effective. Direct path: banner layer emits pure vector (paths, gradients, glyph **outlines**); the lens layer is RB → **III.3 pipeline**: below-stack re-rendered to raster `B` at 4 px/unit (idempotence-licensed second walk), `L` initialized from `B`, lens tree rendered to raster, lerp-merged, region embedded as one image; any layer above continues vector. The lens's `Luminance+invert` mask inside the raster walk uses the single-matrix realization (II.6) — mask emission checks (SK-R12) are moot inside a raster embed. Expected file structure, checkable: vector content below, **exactly one** embedded raster region for the lens, vector above — the extended structural check (Appendix C) counts it. Invariant: nothing silently became `SrcOver`.

**S6 — 40 000 hydrogen-orbital splats, live.** `PointBatch2D{ Positions[40k], Scalars[40k], Transfer = viridis LUT, Size = 3 }` → per-scalar path (II.4): transfer loop fills the pooled `SKColor[40k]` (160 KB, reused), RSXforms fill the pooled array (640 KB, reused), **one** `DrawAtlas` over the 128 px disc, `Modulate`. Budget on the Victus 15: the transfer loop is ~40k LUT lookups (≪ 1 ms); one managed draw call; Skia batches the quads internally. Invariants: §1.4 honored (no per-point managed calls); frame 2+ allocates **zero** (arrays sized at first sight — one topology event); vector export of the same node routes to the portable loop and is the documented `VectorPolicy.Raster` candidate (III.5 bulk row).

**S7 — `SceneNode` embedding.** The child shares the provider and pool through its wrapped environment. Under `RenderPolicy.Always`, the child renders each parent render and the transient snapshot is disposed inside the call. Under `WhenInvalidated`, the node owns a persistent cached image/target, redraws only after `InvalidateRender()` or automatic target-spec invalidation, and reports its bytes separately. `Once` renders on first use and rejects later explicit invalidation. Tick gating remains independent of render caching.

**S8 — Linear-light glow layer.** A layer sets `SurfaceSpec { ColorSpace = LinearSrgb }` (samples inherit O's = 1): target = **F16 premul, linear-tagged** — at `s = 4/3` that is 2560×1440×8 B ≈ 29.5 MB, the budget table's ×2 row. Additive (`Plus`) glow strokes accumulate in linear light — physically-plausible bloom, the reason the opt-in exists (§3.4). Merge into sRGB A: the tagged `DrawSurface` converts — no engine code. Note carried from §3.4: opacity fades of this layer's *content* happen in linear light and look different from sRGB-space fades — intended, documented. Invariant: FP-1 is correctly **ineligible** (spec mismatch with O) and the canonical path engages without ceremony.

## Appendix C — Test obligations

Extends architecture §18 and NAJM-COMPOSITOR §10; every item is an executable obligation:

- **Letterbox round-trip property test** — `virtual → window → virtual` identity within ε across aspect ratios and DPI ratios (`fb/win ∈ {1, 1.25, 1.5, 2}`), including out-of-letterbox points (unclamped).
- **Capture crop + overlay exclusion golden** — a captured frame equals the content rect exactly (no bars) and lacks the overlay while the screen shows it.
- **MSAA-4 equivalence rows** — no-op equivalence and FP-1 ≡ canonical under `Msaa = 4` (pins SK-R05).
- **Feathered-line tolerance golden** — per-segment-colored batch vs. the portable per-segment reference within stated tolerance at several scales (II.4).
- **Backdrop scratch goldens** — region confinement, opacity independence, and **edge-clamp at surface boundaries** (the clamp-pad strips), vector-pipeline counterpart = structural golden **vp-09**; RB direct-path pipeline golden **vp-10** (raster region content matches the composited-path render of the same stack within resample tolerance).
- **Extended `VectorPolicy` structural check** — counts embedded images per export and probes for native blend/mask/group-opacity constructs, driving SK-R10…R12's verdicts from real files (III.5's reading rule made executable).
- **Interop wrap smoke** — author GL texture → `WrapGlTexture` → drawn → disposed → texture deleted, under the I.7 ordering; asserts the `GpuBacked` attach check fires on raster caps.
- **Shutdown-order test** — full IV.12 sequence executes without native faults; provider asserts zero outstanding surfaces.
- **Warm-restart integration** — the manual restart command disposes the current scene, re-invokes the factory, retains the environment/provider, and resumes with fresh coroutine state.
- **Pool steady-state** — existing COMPOSITOR §10 obligation, re-cited here because made the backdrop scratch its permanent customer: N warm frames with a live frosted panel ⇒ zero allocation, zero pool events beyond the acquire/release pair hitting cache.
- **The SK-R registry itself** — Appendix A.2 is a test list; a version bump that flips any verdict fails CI until the consuming section's fallback is toggled.
