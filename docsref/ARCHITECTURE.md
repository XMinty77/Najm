# Najm — Engine Architecture

**Status.** Current architecture baseline for Najm. This document owns identity, author-observable semantics, lifecycle, and portability contracts. Realization details live in `NAJM-COMPOSITOR.md`, `NAJM-SKIA.md`, and `NAJM-TEXT.md`; implementation sequencing lives in `ROADMAP.md`. The documents describe the current design only.

---

## 1. Identity, goals, non-goals

**Najm is a C# engine for vector-crisp, math-literate, real-time interactive graphics, aimed at education.** It occupies deliberately unclaimed territory: Manim owns programmatic math video but is offline and non-interactive; Motion Canvas is generator-procedural with reactive properties but web-only, 2D-only, and video-first; general game engines provide real-time interactivity but fight vector crispness, typesetting, and plotting, and carry enormous conceptual overhead for "a slide with a slider on it." Najm codes like a game engine and draws like a figure.

### 1.1 Primary workflows

1. **YouTube video graphics.** Najm produces crisp procedural animation *clips* via deterministic offline rendering; a traditional video editor owns final production. Najm is not a video editor. The archetype: code a sorting-array visualization once; feed any data and any algorithm; it looks beautiful every time with zero reanimation effort. Interactive sessions can additionally be captured live to video (§4.6) — capture is a recording of the session, not a deterministic render.
2. **Interactive presentations.** Native-first "PowerPoint-as-a-video-game." Presentations are **not an engine feature**: the engine provides embedding, signals, and deterministic replay (§14, §10); presentation tooling (decks, transitions, back-navigation) is library code built on those primitives.
3. **Publication figures.** SVG/PDF vector export of any portable scene — including **vector 3D figures** via the projection pipeline (§8) — for LaTeX papers. A first-class goal. Dense content embeds as raster inside vector output under explicit author control (§7.6).
4. **Heavyweight web demos** *(deferred)*. One fullscreen interactive demo per page (e.g., a sorting-algorithm stepper with user-input arrays and a guess-the-step game mode). Web work is deferred entirely; the architecture preserves feasibility as *passive properties*, at zero present cost.
5. **Real-time simulation front-end** *(deferred)*. Out-of-process simulations (Julia/C++) feed the scene via shared memory; the host publishes snapshots as Ambients (§4.6, §13). Interfaces designed at M5, implemented later.

### 1.2 Design pillars

1. **Codes like a game engine.** Imperative authoring: a node tree, attachable behaviors, immediate-mode drawing in `Render(ctx)`, and coroutines as the primary scripting workhorse. No reactive-signal / animatable-property system.
2. **Two runtime modes, one scene model.** Every scene runs *live* (variable dt) or *deterministic* (fixed dt) without engine-level friction (§2). Live and offline **variants of a production** are expected to be different thin programs over a shared core (§2.5).
3. **Determinism is a pillar, not a feature.** Offline rendering, replay-based presentation navigation, golden-image testing, and reproducible science all hang from it. It ships in M1.
4. **Semantics over backend.** Rendering contracts — especially 3D compositing (§8.2) and the composition model (§6.7) — are defined mathematically, never as "whatever Skia does." Skia is the powerful realization of the 2D contract and a convenient realization of parts of the 3D one; each backend documents its fidelity. `Najm.Core` references no backend; authors who want raw Skia power opt in per-drawable with attach-time safety (§7.5).
5. **Host/Scene split.** Scenes are portable programs; hosts own platforms (windows, clocks, encoders, files). Nothing platform-shaped exists in Core (§4).
6. **Iteration speed is a feature.** The hot-reload dev loop — method-body edits applied in place, warm fresh-restart for everything else — is a milestone exit criterion, not a hope (§15).
7. **Beauty by default.** High-quality AA, OKLCH-aware color, an explicit color-management policy (§3.4), a **declarative composition model** — masks, blend groups, effect graphs (§6.7) — a serious easing library, LaTeX typesetting.
8. **Predictable frame-time behavior.** Core steady-state paths target zero managed allocation; transition and offline paths use explicit budgets (§3.6).

### 1.3 Non-goals

- **Not a video editor.** No grading, no timeline UI, no scrubbing tools. Export clips; edit elsewhere.
- **Not an arbitrary desktop UI toolkit.** Widgets exist to serve scenes (sliders over worlds), not applications. User window-resizing is not a design target (§5.1). The Layout phase (§6.8) is transform resolution, not a constraint solver or flexbox.
- **Not a simulation host.** Light per-frame computation is welcome; CFD-grade work lives out-of-process behind IPC.
- **No declarative authoring layer or visual editor** in the engine. A declarative library or editor that *generates scene code* is a plausible future ecosystem project, out of scope for years.
- **No non-programmer authoring.** C# competence is assumed; the author base for the foreseeable future is the owner plus AI coding agents.

### 1.4 Performance budget

- **Live baseline:** a few hundred to ~3,000 nodes per scene at 60 FPS on the baseline laptop (HP Victus 15). Most live scenes will sit far below this.
- **Bulk data:** 10⁴–10⁵ points — 2D scatters and quivers as much as 3D clouds (e.g., a 40k-sample hydrogen-orbital cloud with a free camera) — must flow through **batch primitives** (§7.3, §8.2): one node, one draw call — never per-point nodes or per-point managed calls. This rule binds the engine's own internals too (§8.3 flushes through the bulk tier).
- **Composition brackets are fill-rate cost.** Every isolation bracket (§6.7) is an offscreen pass bounded by the node's device-resolved visual bounds; the debug overlay counts live brackets per frame (§15). Bracket-happy scenes are the composition model's foot-gun.
- **Offline/static renders** may exceed live budgets freely (workstation available: Core Ultra 7 + RTX A5000).
- **No GC hitches:** see §3.6.

### 1.5 This document and its companions

This document defines the current **author-observable architecture**: identity, runtime semantics, lifecycle, composition algebra, coordinate spaces, portability rules, and failure behavior.

- **`NAJM-COMPOSITOR.md`** — target ownership and pooling, isolation brackets, layer accumulation, backdrop realization, fast paths, and compositor diagnostics.
- **`NAJM-SKIA.md`** — Skia surface/provider realization, drawing and effect lowering, vector export, desktop GL host, capture, media sinks, and binding checks.
- **`NAJM-TEXT.md`** — text handles and layouts, shaping, math, rich text, fragments, text-on-path, caches, and backend lowering boundaries.
- **`ROADMAP.md`** — implementation order, milestone scope, acceptance clips, and deferred work.
- Future companions may cover native 3D and IPC when those milestones begin.

**Boundary rule:** anything a scene author can observe belongs here. Companions may choose implementation strategies only where those choices preserve this document's semantics.

## 2. Runtime modes, determinism, delivery

### 2.1 The two modes

| Mode | Clock | Time source | Input | Use |
|---|---|---|---|---|
| **Live** | `ClockPolicy.Live(maxDt)` — variable dt, clamped | wall clock | full input pipeline (§9) | interactive demos, sim front-ends, live presentation layer |
| **Deterministic** | `ClockPolicy.Fixed(fps)` — fixed dt, frame-indexed | simulated time | **none — empty `InputBlock` by contract** | offline video/still render, replay, golden tests, presentation stepping |

`ClockPolicy` is a **host** parameter (never a process-global): a presentation host may run live while ticking embedded scenes fixed-step; an offline export and an interactive preview of the same scene class can coexist in one process; a `SceneNode` can pause or slow-mo its child by retiming the `TickContext` it forwards. Scenes that need to know read `tick.Time.IsFixedStep`.

There is **no** fixed-update-plus-interpolation (Gaffer accumulator) machinery. Two modes suffice for this domain.

### 2.2 Determinism is a scene discipline the engine enables

Guaranteed by the engine: fixed-step double-precision time (§4.3), contractual iteration order everywhere (§6.5), a coroutine scheduler with fully specified semantics driven by sim time (§10), deterministic cancellation cleanup (`Cancel` disposes enumerators so `finally` runs — §10.4), asset I/O confined to load (§4.4), **no input in deterministic runs** (§2.1, §9.1), and **signals as the sole external stimulus** — driver-raised, loggable, and re-raisable for replay (§10.5). Upheld by the scene: no wall clock, no unseeded RNG (scenes own an explicitly seeded `Random`), no order-dependent external I/O. See checklist, Appendix A.1. A live variable-dt run and a fixed-dt replay reach identical states only for time-parametric logic (tweens, `f(t)`); stateful per-frame integration diverges between modes — acceptable and documented.

**Replay recipe** (presentations, previews, seek): construct a **fresh scene instance**, load it, run fixed-step ticks to the target time — re-raising any signal-log entries `(frame, signal)` at their frames — and suppress rendering until arrival. This is the presentation back-navigation primitive and the basis of a fixed-step seek utility. Beat checkpointing remains an optional optimization under the same determinism contract.

**Reproducibility fine print.** Byte-identical CPU-raster frame hashes hold **per pinned environment** (OS, CPU family, .NET version, Skia version, HarfBuzz version, CSharpMath version, Unicode data version, and the pinned font bytes themselves; the Full math flavor adds the TeX distribution stamp, §12.5): .NET's transcendental functions are libm-backed and vary across platforms, and Skia dispatches SIMD blitters at runtime. Golden-image testing therefore pins its environment (or stores per-platform goldens); cross-machine byte-equality is not promised (§18).

### 2.3 Frame–time convention (normative)

- Under `ClockPolicy.Fixed(fps)`, **tick k** (k = 0, 1, 2, …) carries `Dt = 1/fps` and `Elapsed = (k+1)/fps` — the sim time the tick advances **to**, and the time the subsequent render depicts. **Output frame k is the render performed after tick k.**
- A **still at `t`** = run `ceil(t·fps)` ticks, then render once. `at: 0` renders the loaded state with **zero ticks** — `OnLoad`/`OnStart`-established state, no `Dt` ever consumed.
- Live mode carries the same post-advance meaning: `Elapsed` accumulates clamped `Dt`.
- `Frame` starts at 0. `OnStart` runs inside the first Tick, before the Input phase (§4.7), so routines started there are queued before the frame's coroutine pass (§10.2).

This convention names output files, anchors `VectorExporter(at: t)` seeking, and keeps `Wait.Seconds` arithmetic exact: 0.5 s at 60 fps is exactly 30 ticks (§10.3, §18).

### 2.4 Delivery table

Only the host/driver changes across rows; a scene *class* may serve several rows, and a production typically ships thin variants over a shared core (§2.5).

| Delivery | Driver | Surfaces | Draw context | Output |
|---|---|---|---|---|
| Desktop live | `DesktopHost` | GL-backed Skia | `SkiaDrawContext2D` | window swap |
| Desktop live + capture | `DesktopHost` (+ `Capture`) | GL-backed Skia | `SkiaDrawContext2D` | window swap + frame tee → sink (§4.6) |
| Offline video / still | `OfflineRenderer` | CPU raster Skia | `SkiaDrawContext2D` | frame sink (PNG sequence / encoder pipe) |
| Figure export | `VectorExporter` | none (direct path) | `SkiaDrawContext2D` over Skia SVG/PDF canvas | `.svg` / `.pdf` |
| *(future)* Web live | `WebHost` | WASM Skia | `SkiaDrawContext2D` | browser canvas |

A **static render** is a deterministic run sampled at one `t` per §2.3. **Audio** in deterministic runs is captured as a cue list for editor muxing (§11).

### 2.5 Live and offline are different programs sharing a core

The engine does **not** promise that one scene runs unmodified as both an interactive session and a rendered clip. Interactive pacing (clicks, signals, free cameras) and rendered pacing (timed choreography) are different top-level programs; pretending otherwise produces scenes full of mode branches. The engine's job is to make the **shared parts large and the swapped parts thin**:

1. **Constructor parameterization is the blessed idiom, delivered through scene factories.** Hosts and drivers take scene *factories* (`Run(() => new EpicycleScene(terms: 50))`), so parameters, data, and mode flags are plain constructor arguments surviving verbatim inside the lambda — no registry, no configuration layer. The factory is also what warm fresh-restart re-invokes (§15); `Run(Scene instance)` remains as sugar for single-run use and **disables restart** (logged notice on the first rude edit).
2. **Core + variants.** The sanctioned pattern: an abstract core scene owns the tree, the visuals, and the reusable sub-choreographies (coroutines yielding `Wait`s); thin variants (`…Live`, `…Clip`) own only the top-level driver coroutine. See Appendix B.2.
3. **Nested coroutines are the unit of sharing.** `Wait.For(SubChoreography)` lets both variants replay identical beats with different pacing around them.
4. **Pacing helpers are library taste.** For the simple cases that *do* unify (a deck slide that waits for a click live and a fixed delay in autoplay), `Najm.Guard` may ship pacing helpers; the engine assumes nothing.
5. **Deterministic runs take no input** (§2.1). A scene intended for offline render must not depend on the input pipeline; interactive behavior belongs to a live variant.
6. **Recording an interactive session is live capture** (§4.6), a host feature — not a deterministic render, and not input replay.

---

## 3. Conventions (normative)

### 3.1 Platform & naming
- **TargetFramework: `net10.0`** (current LTS). SDK pinned via `global.json`.
- `<Nullable>enable</Nullable>` everywhere; **warnings-as-errors in `Najm.Utils` and `Najm.Core`** at minimum.
- Math types: `System.Numerics` (`Vector2/3`, `Quaternion`, `Matrix3x2`, `Matrix4x4`).
- **Naming convention:** template hooks that authors override are **`On`-prefixed** (`OnLoad`, `OnStart`, `OnStop`, `OnUnload`, `OnAttach`, `OnDetach`, `OnBeforeRender`, `OnAfterRender`); engine-invoked commands and author-called verbs are bare (`Tick`, `Render`, `Start(routine)`, `Animate`). This is what disambiguates `Scene.OnStart` (hook) from `Scene.Start(routine)` (command).

### 3.2 Math conventions
- **Row-vector convention** (this is what `System.Numerics` implements): points transform as `v · M`; translation lives in the last row (`M31/M32` for `Matrix3x2`, `M41..M43` for `Matrix4x4`).
- **2D transforms are `Matrix3x2`; 3D transforms are `Matrix4x4`** (§6.3). **In both: `WorldMatrix = LocalMatrix * Parent.WorldMatrix`** (local applied first). A unit test **per matrix type** asserts a translated parent + rotated child lands at the known world position; these tests are the convention's guardians.
- **2D and 3D never compose at the matrix level.** All 2D↔3D traffic flows through cameras and virtual space (`WorldToVirtual`, `VirtualPointToRay` — §8.4); space homogeneity is enforced at attach (§6.1). This rule is what makes the split transform types safe.
- **Right-handed** coordinate system.
- **Y-up in world space** (2D and 3D — matches textbooks and plots). **Y-down in virtual space** (origin top-left). The flip lives **exclusively** in cameras/projection — never in nodes, never smeared across surfaces.
- Angles are radians internally; an `Angle` value type provides `Angle.Deg(x)` / `Angle.Rad(x)` at API surfaces.
- `System.Numerics` projection helpers produce D3D-style **[0,1] depth NDC**. Irrelevant to the CPU projection backend (which needs only screen XY + a depth key); the future `GpuDrawContext3D` converts NDC conventions in **exactly one place** (itself).

### 3.3 Coordinate spaces (closed vocabulary)
1. **Local** — a node's own space; all drawing in `Render` happens here. All sizes — strokes, text, batch footprints, effect parameters — are local units (§7.4).
2. **World 2D / World 3D** — per-layer, Y-up; cameras map world → virtual.
3. **Virtual** — the scene's presentation space, fixed resolution (default **1920×1080**), Y-down, origin top-left. `ScreenLayer` lives here; input arrives here; all scaling to real outputs happens outside the scene (§5.1).
4. **Window** — physical pixels; exists only inside hosts.

Camera mapping APIs use this vocabulary: `WorldToVirtual` / `VirtualToWorld` (2D and 3D) and `VirtualPointToRay` (3D). "Screen" appears only in the type name `ScreenLayer` (the layer that lives in virtual space) and in prose about sizing, where "constant screen size" means **constant in virtual units** — achieved per node via scale pinning (§6.3) or per quantity via the `ctx.Scale` idiom (§7.4).

### 3.4 Color — policy, not vibes
- Backend-agnostic `Color` in `Najm.Utils`: sRGB-referenced RGBA storage with HSL and **OKLCH** constructors/converters and linear-space conversion helpers. No `SKColor` in public API.
- **Every surface is explicitly tagged with its color space** (via `SurfaceSpec`); untagged surfaces do not exist. **Alpha is premultiplied everywhere** past the API boundary.
- **The initial color-space vocabulary is closed at `{ Srgb, LinearSrgb }`** (a Core enum; extensible deliberately, Display-P3 being a future entry). **Pixel format derives from the space:** `Srgb → RGBA8888 premul`, `LinearSrgb → RGBA F16 premul` — linear light in 8 bpc bands is silently wrong, and the F16 doubling is the cost §3.6's budget table already carried.
- **The compositor merges in the output target's color space.** **An unset layer `SurfaceSpec` inherits the output target's *full* spec — color space *and* sample count** — making the common case conversion-free **and** fast-path-eligible (§5.3) by default; a layer overrides space and/or samples deliberately (e.g., linear for physically-correct blur/glow), and Skia's color-space-aware drawing converts at merge time. **Size is never author-specified** — always engine-computed (§5.3). Consequence, stated so it never surprises: layer-`Opacity` fades and crossfades are computed in the merge space, and linear vs. sRGB fades differ slightly.
- Composite and blend in **linear space where the surface supports it**; sRGB at the edges. Gradients/blur/glow in pure sRGB are known-wrong; prefer correctness when available.
- **Vector export is sRGB, period** (SVG/PDF interop reality).

### 3.5 Threading: a phase contract, not a thread contract
- The engine **must run entirely single-threaded** (a hard web requirement and the simplest correct mode). Phases are ordering guarantees, not thread assignments.
- `Update` (and all of `Tick`) never touches GPU/Skia objects. Whichever context calls `Render` owns all Skia/GL handles for that target.
- If a host introduces threads (e.g., render thread + IPC), the host owns the handoff; Core stays agnostic.
- **Author compute kernels may parallelize** (`Parallel.For` over independent elements) without violating this section or determinism, provided results are order-independent — per-pixel/per-element writes to disjoint indices qualify. §3.5 constrains the engine; deterministic outputs constrain the author.

### 3.6 Allocation and resource discipline

Najm optimizes for **no GC hitches**, not for a universal prohibition on allocation.

**Hard zero-allocation targets after warm-up:**

- Core scene traversal, transform propagation, culling, and layout scheduling.
- Stable compositor topology: unchanged layers, bracket population, target sizes, and surface specs.
- Built-in batch nodes, static text drawing, common tweens, and coroutine polling.
- Input-buffer reuse and steady-state debug accounting.

**Transition allocations are allowed and measured:**

- `OnLoad`/`OnAttach`, structural mutation, coroutine or animation creation.
- New text or markup content, first typeset, cache growth, and changed dynamic layout capacity.
- First appearance of a target size, bracket shape, surface spec, or composition topology.
- Capture queue growth, asset decode, shader compilation, and other explicitly cold operations.

**Offline paths may allocate freely when it improves simplicity or correctness:** vector export, still rendering, encoding setup, diagnostic reports, and test harnesses.

The debug overlay reports managed bytes per frame, GC collections, content transitions, pool events, provider surface estimates, and Skia's independent resource-cache usage. Provider-created surfaces remain outside Skia's resource-cache budget so ownership and accounting do not overlap. Performance tests enforce the hard targets on representative steady-state scenes rather than treating every future feature as allocation-forbidden by definition.

## 4. Host / Scene / Environment model

The central structural idea: **scenes are portable programs; hosts are platforms.** A scene needs time (and, live-only, input) per tick, a target per render, and five stable capabilities — nothing else. There is no service registry: engine capabilities are a **closed, typed set** (the environment); author-defined ambient data is the **open** set (Ambients, §13). `IWindowManager`, `IInputManager`, `IRenderManager`, `IEffectsManager`, and service-locator patterns generally **do not exist**.

### 4.1 Scene contract

Lifecycle transitions are engine-controlled commands; author code overrides protected hooks. A host, embedder, or test cannot call hooks out of order.

```csharp
public class Scene
{
 public LayerStack Layers { get; }
 public AmbientRegistry Ambients { get; }
 public Size VirtualResolution { get; init; } = new(1920, 1080);
 public SceneEnvironment Env { get; private set; } // valid while loaded

 internal void Load(SceneEnvironment env);
 internal void StartFrame();
 internal void Stop();
 internal void Unload();

 public void Tick(in TickContext tick);
 public void Render(IRenderTarget target);
 public void RenderDirect(IDrawContext2D context);

 protected virtual void OnLoad() { }
 protected virtual void OnStart() { }
 protected virtual void OnStop() { }
 protected virtual void OnUnload() { }

 public CoroutineHandle Start(IEnumerator<Wait> routine);
 public AnimationHandle Animate(...);
}
```

`Load` validates the state transition, binds the environment and compositor, then invokes `OnLoad`. `StartFrame` invokes `OnStart` exactly once inside the first tick. `Stop` and `Unload` run at most once and release scene-owned scheduling/composition state even when an author hook throws.

**Lifecycle:** `Construct → Load → Start → (Tick/Render)* → Stop → Unload`, one pass. Scene instances are cheap; reset and replay construct a fresh instance. Hosts accept factories so constructor parameters are retained across manual warm restart.

**Single-driver rule:** every scene instance has exactly one driver — a host or a `SceneNode` — and is ticked at most once per frame. Reusing a scene class means constructing another instance.

**Render idempotence:** `Render` and `RenderDirect` do not mutate observable scene state and produce identical output from identical pre-render state. Lazy matrix caches, backend caches, and first-use target acquisition are permitted only when they cannot alter output or author-visible state. This contract enables vector re-walks, embedded scenes, multi-view rendering, and render-twice verification.

### 4.2 SceneEnvironment — the closed capability set

```csharp
public sealed class SceneEnvironment
{
 public IAssets Assets { get; } // §4.4
 public ITypesetter Typesetter { get; } // §12
 public IAudioSink Audio { get; } // §11
 public ISurfaceProvider Surfaces { get; } // §4.5
 public RenderCaps Caps { get; } // §4.5

 public SceneEnvironment With(IAudioSink? audio = null, ...); // decorator wrapping (§14.2)
}
```

Closed on purpose: five typed properties are compile-time discoverable and trivially wrappable by embedders. New engine capabilities are added here deliberately; everything author-shaped goes through Ambients.

**Assembly recipe: hosts assemble what they own and inject what they don't.** `DesktopHost` constructs `Assets`, `Surfaces`, `Caps` natively; **`Typesetter` and `Audio` arrive via `HostOptions`** — the host projects reference neither `Najm.Text` nor `Najm.Audio`, keeping §16's dependency rows true. Core ships fail-loud null objects as defaults: **`NullTypesetter`** (throws on first use, naming the option to set) and **`NullAudioSink`** (no-op). `Najm.App` — the only place hosts are constructed — passes the real implementations. Without a stated composition recipe, every driver signature in §4.6 would quietly imply Core referencing backends; injection with fail-loud defaults keeps the dependency rows true and the one-liners honest.

### 4.3 TickContext — per-tick pure data

```csharp
public readonly struct TickContext
{
 public TimeInfo Time { get; } // double Elapsed, double Dt, long Frame, bool IsFixedStep (§2.3)
 public InputBlock Input { get; } // events + snapshots, in VIRTUAL coords (§9); EMPTY in deterministic runs
}
```

`TimeInfo` uses **double** for `Elapsed` and `Dt` (long fixed-step runs must not drift; float accumulation is a known trap) and **long** for `Frame`. Geometry stays `float` via `System.Numerics`. `Elapsed`/`Frame` semantics are fixed by §2.3.

Passed with `in` (by-reference readonly — no copying; `readonly struct` prevents defensive copies). `Scene.Tick` stores it **once per frame**; nodes read scene-level accessors or receive `in` parameters. `InputBlock` references per-frame **pooled** buffers (cleared and refilled, never reallocated). Deterministic drivers supply a canonical **empty** block (§2.1). A struct rather than a class purely for allocation: zero per-frame heap objects.

### 4.4 IAssets and portable handles

`IAssets` loads and caches shared resources behind backend-neutral handles: `IImage`, `FontFace`, `IAudioClip`, `IPath`, and capability-gated shader handles. Asset I/O is confined to load/attach transitions; nodes retain handles, never backend objects.

Portable handles contain the information needed to recreate backend realizations. A `FontFace`, for example, carries source identity, retained bytes, and a face index. Backend modules keep their native realizations in **backend-owned side tables** keyed by the portable handle. The same handle can therefore be realized by multiple backends or contexts without a mutable `object Interior` slot, cross-module write races, or ambiguous disposal ownership. Paths follow the same rule: Core retains path verbs/geometry; Skia caches `IPath → SKPath` privately.

Text layouts likewise contain only portable run data and portable `VectorPicture` commands. Skia owns `ITextLayout/run → SKTextBlob`, glyph-path, and picture caches. See `NAJM-TEXT.md` and `NAJM-SKIA.md`.

**Pixel readback:** `IImage.CopyPixels(Span<byte> destination, PixelFormat format)` is the low-level synchronous capability. `PixelFormat` includes `Rgba8888`, `Rgba8888Premul`, and `Bgra8888Premul`. GPU images may synchronize and read back; this is deliberately absent from the ordinary render loop.

### 4.5 ISurfaceProvider, IRenderTarget, RenderCaps

```csharp
ISurfaceProvider.CreateTarget(in SurfaceSpec spec) → IRenderTarget
ISurfaceProvider.CreateCompositor() → ICompositor

// SurfaceSpec: pixel width/height, sample count, mandatory color-space tag
// IRenderTarget: Size, SurfaceSpec, GetContext, Snapshot, IDisposable
```

Surface quality is configuration at this boundary. Raster providers normalize sample count to 1 because CPU raster antialiasing has no multisample target axis. Targets are persistent and reused; the compositor owns layer and accumulation targets, while the provider owns reusable scratch surfaces.

`RenderCaps` flags include `SkiaSurface`, `VectorTarget`, and `GpuBacked`. Backend-specific drawables validate required capabilities at attach time.

### 4.6 Hosts, capture, and frame sinks

A host is the composition root. It assembles the environment, owns the clock and platform event pump, feeds ticks, provides render targets, and delivers output. Before each tick it synchronizes platform input and future external snapshots into Core abstractions.

- **`DesktopHost`** owns the Silk.NET window and GL context, letterboxing, input conversion, presentation, debug overlay, capture, and warm restart. It constructs the GPU Skia provider over the current GL context.
- **`OfflineRenderer`** is a deterministic fixed-step Core loop over an injected environment and an empty `InputBlock`.
- **`VectorExporter`** evaluates a fresh scene at a selected time and drives `RenderDirect` through an injected `IVectorTargetWriter`.

Hosts and convenience drivers accept scene factories:

```csharp
new DesktopHost(options).Run(() => new PhononScene(seed: 7));
SkiaOffline.Render(() => new PhononScene(seed: 7), offlineOptions);
SkiaExport.Pdf(() => new OrbitalFigure(), "fig3.pdf", at: 2.0);
```

### Capture boundary

Capture snapshots the **letterboxed content region before swap and before the debug overlay**, then copies pixels into a pooled owned buffer. `IImage` and render-target snapshots never cross the capture call boundary.

```csharp
public interface IFrameSink
{
 void Begin(in FrameStreamInfo info);
 void Submit(long frame, PixelFrameLease pixels); // ownership transfers to the sink
 void End();
}
```

`PixelFrameLease` carries dimensions, stride, format, and pooled memory; the sink must dispose it after synchronous use or after an asynchronous encoder has consumed it. This makes queueing and backpressure explicit and avoids retaining a snapshot whose backing surface will be written next frame. Capture reports dropped frames when a bounded asynchronous queue is full.

Concrete PNG and ffmpeg sinks live with the Skia/media realization. Recording remains a host concern; scenes never know capture or IPC exists.

### 4.7 The frame, end to end

```text
DesktopHost.Run(make):
 env = assemble capabilities
 scene = make; scene.Load(env)
 while window open:
 pump events and map window → virtual coordinates
 advance clock and publish external snapshots
 scene.Tick(tick)

 clear letterbox bars
 scene.Render(contentTarget)

 flush GPU work
 if capture: snapshot content → pooled PixelFrameLease → sink.Submit
 if overlay visible: draw overlay; flush
 swap buffers
 scene.Stop; scene.Unload
```

Capture is pre-swap because reading a swapped default framebuffer is not portable. Drawing the overlay after capture keeps recordings clean.

```text
Scene.Tick(in tick):
 assert tick-once; store tick
 if first tick: StartFrame

 INPUT: route input; flush structural mutations
 UPDATE: layer/node/behavior updates; tween pass; coroutine pass; flush
 LAYOUT: registered participants by rank and tree order; flush
```

The tween pass precedes coroutine polling so `Wait.For(animation)` chains without a blank frame. A `SceneNode` drives its child inside the parent's update position and renders it from the node's render path or cache policy (§14).

## 5. Virtual resolution, layers, compositor

### 5.1 Virtual resolution — the single scaling point

`VirtualResolution` is a **Scene-level** property (default 1920×1080). `ScreenLayer` coordinates *are* virtual coordinates; world cameras frame against the virtual aspect. Hosts letterbox virtual→output preserving aspect and inverse-map input; letterbox bars are cleared to **`HostOptions.BarColor`** (default opaque black) by the host each frame, outside the content rect. `SceneNode` embeds against the child's virtual size. One scaling point serves rendering, pointer math, and embedding identically. Web demos follow the same rule (fixed resolution scaled to fit the page). Authors wanting arbitrary responsive behavior handle it themselves; the engine does not chase window resizing.

**Render scale — scale-invariant, variable-quality output.** Output pixel size is a **driver** parameter, never a scene concern. Every driver renders the virtual space at `RenderScale = output / virtual`: `DesktopHost` derives it from the letterboxed window (hi-DPI falls out for free); `OfflineRenderer` and `VectorExporter` take an explicit output size or scale (default 1×). The compositor allocates per-layer targets at **scaled** size and installs the scale *below* every author-visible coordinate (§5.3), so scene code always operates in virtual 1080p while the same scene renders a 360p draft preview or a 4K final by changing one argument. All local-unit quantities scale with output; **pinned** subtrees (§6.3) hold constant *virtual* size and therefore still scale with `RenderScale` — that is the invariance. `SceneNode` forwards the effective scale to its child's target, so embedded scenes stay crisp too. Caveats: raster assets soften beyond their intrinsic resolution, and resolution-dependent SkSL must read the context's read-only `RenderScale` instead of assuming pixels; vector content is exact at every scale.

### 5.2 Layers

A `Layer` is: **a coordinate space + an optional camera reference + a root node subtree + a persistent render target + input participation + presentation state**, composited back-to-front in **add order**. Presentation state: `Visible`, `Opacity`, **`Blend`**, **`Effect`**, **`ReadsBackdrop`**, `ClearColor`, optional viewport rect, `RequiredCaps` — the same composition vocabulary nodes carry (§6.7), applied one level up, at the merge (§5.3). This applies to every layer type: a `WorldLayer3D` can carry a merge-time `Effect` like any other layer (by merge time it is an image). An unset layer `SurfaceSpec` inherits the output's **full** spec — space *and* samples (§3.4). Base `Layer` additionally carries **`virtual bool YAxisPointsUp`** (`ScreenLayer` false, `WorldLayer2D` true; custom layers inherit or override) — the layer-level fact text nodes read for the upright rule (§12.3).

- **`ScreenLayer`** — no camera; virtual space; conventionally topmost; **input-first**.
- **`WorldLayer2D`** — `Camera2D` (a `Node2D`: Position/Zoom/Rotation; `CenterOn`, `FitRect` helpers); Y-up world → virtual mapping (the Y flip lives here). Camera zoom scales geometry **and every local-unit quantity together** — the zoom≡crop invariant (§6.3); constant-screen-size content opts out per node via scale pinning, or per quantity via `ctx.Scale` (§7.4).
- **`WorldLayer3D`** — `Camera3D` (§8.1).
- **Backend capability is orthogonal to space — there is no `SkiaLayer` type.** "Which coordinate space" and "which backend power" are independent axes; a Skia×space class matrix would be combinatorial noise. Instead, any layer may set **`RequiredCaps`** (base-`Layer` property, default none) to assert e.g. `SkiaSurface`, validated at load — fail-fast for the whole layer, useful when the layer's own hooks (§5.4), not just its nodes, need Skia. Per-drawable opt-in remains `SkiaDrawable` with its attach-time check (§7.5). Backend-specific creative work therefore happens in whatever space it belongs to: a `WorldLayer2D` with `RequiredCaps = SkiaSurface`, a `ScreenLayer` full of `SkiaDrawable`s.
- Optional **viewport rect** per layer (e.g., a heatmap panel occupying a screen region); the input router maps pointer coordinates through it (§9.2). **Viewport sizing:** a layer with a viewport rect allocates its target at **viewport size × `RenderScale`**; the layer's camera frames the **viewport's aspect**; the merge places the target into the rect **1:1** (no resampling) — crisp by construction, memory proportional to the region.
- The camera reference is **swappable and consulted at render time**, never baked at load — a reserved property of the multi-view seam (§14.4).

### 5.3 Compositor — contracts

The compositor is internal machinery driven by `Scene.Render` / `Scene.RenderDirect`; authors never construct or call it. Its **entire author-facing surface** is: the layer list and its order; per-layer `Visible`, `Opacity`, `Blend`, `Effect`, `ReadsBackdrop`, `ClearColor`, viewport rect, and `RequiredCaps`; the `OnBeforeRender`/`OnAfterRender` hooks (§5.4); the node-tier composition properties it realizes during traversal (§6.7); and, indirectly, quality via `SurfaceSpec`. Compositing is **Skia-level** — there is no GL FBO/fullscreen-quad/post chain in the engine: per-layer creative effects are declarative (`Layer.Effect`) or live inside Skia drawing (SkSL, image filters — §7.5), whole-video grading belongs to the video editor, and surface quality is `SurfaceSpec` configuration.

**Placement — the three-way split.** **Core** defines **`ICompositor`** — the composited-path contract (per-scene, stateful: it owns the persistent per-layer targets and the accumulation surface; `Scene.Render` delegates to it; `Scene.Load` acquires it) — plus the **render traverser** (the paint-order walk, culling, pinning resolution, and the §6.7 isolation predicate and pipeline orchestration, expressed through context bracket operations; the single home of node-tier composition semantics, shared by both paths so they cannot drift) and the **direct path** (`Scene.RenderDirect`, backend-agnostic by construction). The provider exposes **`ISurfaceProvider.CreateCompositor()`** and is therefore the backend's surface-*and-composition* authority; the environment stays closed at five capabilities, embedded scenes share the provider (and its pool) through the wrapped environment. **`Najm.Skia` ships `SkiaCompositor`**, the initial realization, written directly against `SKSurface`/`SKCanvas` — the deliberate full lean into the Skia dependence; a future backend implements `ICompositor` wholesale. `OfflineRenderer` and `VectorExporter` live in Core and compose through the injected provider. Contexts expose **composition bracket operations as a documented backend-facing SPI** (public, marked backend-facing in XML docs; deliberately not `InternalsVisibleTo`), since the Core traverser must drive them and `SkiaCompositor` must drive the traverser. Realization detail — the SPI operation set, the algorithm, the pool contract — is NAJM-COMPOSITOR scope.

**Target lifecycle.** One persistent offscreen target per layer, acquired from `env.Surfaces` on first render and re-acquired only when `ceil(VirtualResolution × RenderScale)` — or, for a viewport'd layer, `ceil(viewport size × RenderScale)` — or the layer's `SurfaceSpec` changes; disposed on layer removal and `OnUnload`. The compositor owns targets and any snapshots taken from them (the merge itself is zero-snapshot — it reads surfaces directly; NAJM-COMPOSITOR §7). **Validity invariant:** draw contexts and snapshots are valid only inside the current render call — stashing either is a contract violation.

**Composited path** (`Scene.Render(target)`) — per visible layer, bottom → top:
1. Bind the layer's target. **If `ReadsBackdrop`:** initialize it with the **merge-so-far of the layers beneath** (converted into this layer's tagged color space); **else** clear to `ClearColor` (default transparent).
2. Install the base transform: `RenderScale` × space mapping (camera world→virtual for world layers; identity for `ScreenLayer`). Everything author-visible sits *below* this transform — which is what makes nodes **and hooks** scale-invariant (§5.1). Scale pinning resolves here, per node, against the layer's camera (§6.3).
3. `OnBeforeRender(ctx)` → depth-first tree traversal (paint order §6.5; bounds/frustum culling §6.6; **node-tier composition brackets realized per §6.7**) → `OnAfterRender(ctx)`.

**Merge — staged accumulation (normative):** the composited path merges through a compositor-owned **accumulation surface** allocated at output size in the output target's `SurfaceSpec` (thus the §3.4 merge space by construction), cleared each render. Visible layers partition into **runs split before each `ReadsBackdrop` layer**; each run's layers render and merge in order; one final **1:1 replace-blit** to the output ends the frame (the output's prior contents never matter — determinism holds regardless of what the host hands us). An RB-free frame degenerates to a single run, so the staging machinery costs nothing when unused. **Progressive merge into the output is deferred** (predicate: no visible RB layer ∧ output accepts direct composite). Per layer, at its merge into the accumulation — with `Layer.Effect` (if set) applied to the layer content during the draw, sampling **decal** (transparent) beyond the layer image, its parameters read as **virtual units** scaled by `RenderScale` at the merge (node-tier `Effect` parameters remain local units under the CTM, §6.7), into the viewport rect **1:1** if set, color-converted into the merge space by the tagged-surface draw:
- **Ordinary layer:** source-over (or `Layer.Blend`) modulated by `Opacity` — one multiply; also the primitive presentation crossfades want.
- **`ReadsBackdrop` layer** (a run boundary): its target **initializes from the accumulation via replace-draw** (the merge-so-far, converted into the layer's tagged color space), renders, then **lerp-merges**: `out = below·(1−Opacity) + layer·Opacity`. Consequence, free and intended: fading a `ReadsBackdrop` layer's `Opacity` crossfades toward the **unmodified** content beneath — exactly the right semantics for fading an effect in and out. (`Layer.Blend` is ignored on `ReadsBackdrop` layers — the layer *is* the composite.)

Then the host presents/encodes. **Targets clear every render; there is no preserve/feedback mode** — history effects (trails, echoes) are **scene state** (e.g., a pooled ring buffer of positions drawn as a fading polyline or batch), which keeps them deterministic, replayable, and vector-exportable.

**Fast path:** exactly one visible layer, no viewport, `Opacity == 1`, `Blend` default, no `Effect`, not `ReadsBackdrop`, **full `SurfaceSpec` match** with the output — color space *and* MSAA sample count (an MSAA mismatch would make the fast path visibly differ from the canonical path in edge AA, an observable path-dependence; specs compare normalized) → the compositor renders that layer straight into the output target and skips the offscreen entirely. Byte-equivalence with the canonical path is a test obligation (§18).

`Layer.Effect` and `Layer.Blend` are the declarative merge hooks. A raw merge-time shader is added only if real productions demonstrate that the composition algebra is insufficient.

**Direct path** (`Scene.RenderDirect(ctx)`): no per-layer targets — layers walk sequentially into the single provided context, clipped to their viewport rects, `Opacity` applied via `PushOpacity`, layer `Blend` via a context bracket where the target can express it. **A node's `Blend`/`Backdrop` scope is its owning layer — normative semantics on both paths.** The direct path therefore opens a **per-layer isolation bracket** (Skia vector canvases: `saveLayer`; it survives as a PDF transparency group / SVG group per the NAJM-SKIA fidelity table), with an enumerable skip condition: the layer's subtree contains no node with `Blend ≠ SrcOver` and no node with `Backdrop ≠ null`. (Without the bracket, a blending node in layer *k* would composite against layer *k−1*'s pixels — an observable divergence from the composited path.) Node-tier composition realizes through the same context (Skia's vector canvases carry `saveLayer`, clips, and the portable blend subset). **Raster-space features — `Effect` graphs, `Backdrop`, `ReadsBackdrop`, masks beyond what the vector backend expresses — degrade under `VectorTarget` per the normative pipelines of §7.6**, never by silent downgrade. Used by `VectorExporter` (offscreens would rasterize vector output) and as a no-compositing fast path.

The future GPU-3D backend reintroduces render-to-texture **privately inside itself** and hands the compositor a texture-backed image (§8.5).

### 5.4 Layer extension contract

Layers are open for inheritance — but they are the **fourth** customization tool, not the first. Sanctioned hierarchy: (1) subclass `Drawable` for things that draw; (2) attach `Behavior`s for logic — camera motion belongs on camera nodes (`OrbitCameraBehavior`), never in a layer; (3) read `Ambient`s for configuration; (4) subclass `Layer` only when **the space itself** behaves differently. The base class exposes exactly these virtuals: `OnAttach(Scene)` / `OnDetach`, `Update(in TickContext)` (layer-level logic; runs before the layer's tree), `OnBeforeRender(ctx)` / `OnAfterRender(ctx)` — bracketing the tree traversal *inside* the layer's target, with the context already in layer space and scale-invariant (§5.1) — and **`YAxisPointsUp`** (the layer's visual-up fact for the text upright rule, §5.2/§12.3). Legitimate examples: a `GraphPaperLayer : WorldLayer2D` painting an adaptive grid in `OnBeforeRender` from its camera's zoom; a `WorldLayer3D` subclass paired with a custom-projection camera (§8.1). Scenes are *expected* to be subclassed; layers are *allowed* to be.

---

## 6. Node model

### 6.1 Node

`Node`: `Parent`, `Children`, `Behaviors`, `Layer` (the owning layer; valid from attach, null while detached), `Visible`, `Enabled`, lifecycle `OnAttach` / `OnDetach` / `Update(in TickContext)` / `Layout(in TickContext)` (§6.8) / `Render(ctx)`. One node type per space; there is no Entity/Component duality.

- **Transforms live on the space-specific bases, not on `Node`** (§6.3): **`Node2D`** carries a `Transform2D`; **`Node3D`** carries a `Transform3D`.
- **`Enabled = false`** skips the subtree for Update **and** input; still renders. It also makes coroutines and tweens **owned by nodes in that subtree ineligible** — exactly `Pause` semantics (§10.4): their waits stop being evaluated, `Seconds` stops accumulating, tween time freezes; re-enabling resumes where they left off.
- **`Visible = false`** skips the subtree for Render **and** hit-testing; still updates.
- **`Node2D`** additionally carries the **composition properties** — `Opacity`, `Blend`, `Clip`, `Mask`, `Effect`, `Backdrop`, `ZIndex` — defined in §6.7, and `Transform2D.ScaleMode` (§6.3).
- **Space homogeneity is enforced at attach:** a `Node3D` under a `Node2D` (or vice versa) is an error. Cross-space relationships go through anchor behaviors (§8.4), never parenting (§3.2).

### 6.2 Behavior

Attachable logic with lifecycle (`OnAttach`/`OnDetach`/`Update`/`Layout`); owns **no transform**; references its node. *In the tree* = Node (has a transform); *attached logic* = Behavior. Camera controllers, drag handling, anchors are Behaviors.

**The decision rule (normative guidance):** **Behavior = while-attached** — continuous, stateful, node-lifetime logic (follow-pointer, orbit-camera, drag). **Coroutine = until-done** — finite, sequential choreography that reads as sequence. **Tween = a single property ramp.** Each can imitate the others; the scheduler contracts (§10.2 — post-tree pass, drain-to-empty) are why the finite forms execute in the scheduler, never as Behaviors. For tree-visible locality without changing the execution home, `Najm.Lib` ships **`RoutineBehavior`**: starts a given routine at attach, cancels it at detach — sugar over `node.Start`.

### 6.3 Transforms — `Transform2D`, `Transform3D`

- **`Transform2D`** (on `Node2D`): `Position` (`Vector2`), `Rotation` (radians; `Angle` at API surfaces), `Scale` (`Vector2`), `ScaleMode` (below). Caches `LocalMatrix`, `WorldMatrix`, **`InverseWorld`** — all `Matrix3x2`, dirty-flagged with subtree invalidation on change, inverse computed lazily under the same flag.
- **`Transform3D`** (on `Node3D`): `Position` (`Vector3`), `Rotation` (`Quaternion`), `Scale` (`Vector3`). Caches the same trio as `Matrix4x4`.
- **`World = Local * Parent.World`** in both (§3.2). World queries are valid **any time**: `node.WorldMatrix`, `WorldPosition`, camera `WorldToVirtual` / `VirtualToWorld` / `VirtualPointToRay` — this is what unlocks picking, layout, and 2D↔3D anchoring.
- **Cross-node conversion is a composition, not a solve** (enabled by the cached inverse):
 ```csharp
 Matrix3x2 TransformTo(Node2D other) => WorldMatrix * other.Transform.InverseWorld;
 Vector2 ToLocalOf(Node2D other, Vector2 p) => Vector2.Transform(p, TransformTo(other));
 ```
 (Same shape in 3D with `Matrix4x4`.) Usable anywhere world queries are — including Layout hooks, where freshness follows the read-settled/rank rules (§6.8). Cross-**space** conversion does not exist at the matrix level; it goes through cameras (§3.2).
- **Why the split**: a third of the cached-matrix memory and a ~5× cheaper multiply on 2D dirty chains, plus natural 2D ergonomics (`Vector2` position, scalar rotation) — safe because 2D↔3D never composes matrices.

**Scale pinning (`Transform2D.ScaleMode { Inherit, Virtual }`, default `Inherit`).** All 2D sizes — stroke widths, dash intervals, text sizes, batch footprints, effect parameters — are **local units, period** (§7.4); whether they scale under the camera is a *transform-level* choice, not a per-quantity flag:

- **`Inherit`** — normal transform inheritance, and the invariant it buys, stated and tested: *for subtrees that are `Inherit` throughout, camera zoom is equivalent to cropping and uniformly magnifying the rendered image* (**zoom ≡ crop**; golden test, §18). Stroke, glow, and text fatten together with geometry — correct for figure-like content, and what makes a composite shape (fill + stroke + effect) behave as one object under zoom.
- **`Virtual`** — at render/query time, the accumulated linear map from the node's parent frame to virtual space (parent chain × camera) is replaced by **its rotation component × identity**, so **1 local unit = 1 virtual unit at this node**. Precisely: with the accumulated linear part polar-decomposed as `M = R·S`, the pinned frame uses `R`. **Translation is untouched** — the node sits where the tree and camera put it; **rotation inherits**; the node's **own** `Transform.Scale` still applies on top (a pinned label can be made 2×); **descendants inherit the pinned frame**; nesting is **idempotent** (a pinned node under a pinned ancestor is already in virtual units). Pinned content still scales with `RenderScale` (§5.1) — pinning is camera-invariance, not output-invariance. Polar decomposition is exact for the similarity-transform chains that dominate real trees and principled under shear.

**The camera-dependence rule, stated once:** pinning resolves against the layer's camera, so it is **never baked into `WorldMatrix`** — that would break one-tick-many-renders (§14.4). `WorldMatrix`/`InverseWorld` remain the **logical, camera-free** transforms; pinning is applied by the consumers that hold a camera: the render traverser (§5.3 step 2), the input router's point mapping (§9.2), culling, and camera-aware queries — `Layer.ResolveMatrix(node)` / `Layer.ResolveBounds(node)` for tools that need the effective local→virtual mapping. `WorldPosition` stays exact and camera-free even for pinned nodes (translation untouched), which lets `AnchorBehavior` (§8.4) operate without a pinning-specific path. The sharp edge, made visible rather than hidden: a pinned node's **world footprint varies with zoom**, so arrangement helpers against pinned bounds (`NextTo` a pinned label) use **camera-aware overloads** (§6.8, `Najm.Lib`).

**The mixed case** — world-spanning geometry with constant-screen stroke (an axis line) — is a per-quantity fact and gets the per-quantity idiom: divide by the context's accumulated scale, `Paint.Stroke(ink, width: 3f / ctx.Scale)` (§7.4). This idiom lives almost entirely inside `Najm.Lib`'s plot components; a plot user never sees it. **Defaults pre-decided:** `DragHandleNode` and plot tick labels ship pinned; figure content ships `Inherit`.

### 6.4 Registry & deferred structural mutation (unified rule)

The scene keeps a node registry updated on attach/detach **anywhere** in the tree (type queries over nested nodes work). Structural mutations during any phase are **deferred and flushed at the end of that phase** — Input, Update, and Layout each end with a flush (§4.7). At flush, `OnAttach`/`OnDetach` run. **A node attached at a flush participates in all subsequent phases of the same frame:** Input-added nodes Update, Layout, and Render this frame; Update-added nodes Layout and Render this frame ("add a node, see it this frame") and receive their **first Update next frame**; Layout-added nodes Render only. Corollary, stated so it never surprises: **a node can Render before its first Update** — anything `Render` depends on is established in `OnAttach`.

Detach releases input capture (§9.2), cancels node-owned coroutines **and node-owned tweens** (§10.4, §10.6), and unregisters layout participants (§6.8).

### 6.5 Iteration-order contract (determinism depends on this)

- Children **update** in **insertion order**; behaviors in **attach order**; layers in **add order**.
- **Paint order among siblings = stable sort by (`ZIndex`, insertion index)** (§6.7). Render traversal is depth-first pre-order over that order — parents under children. Update and Layout traversal **stay insertion order** even where `ZIndex` reorders paint; both orders are contractual and deterministic.
- **Update traversal:** depth-first pre-order — node's `Update`, then its behaviors, then children. The tween pass, then the coroutine pass, run after the entire tree (§4.7, §10.2), then the Layout pass (§6.8).
- **Hit-test order:** exact reverse paint order (topmost first) within a layer; layers top-down (§9.2).

### 6.6 Drawable contract and bounds

`Render(context)` draws in local space after the traverser has installed the camera- and pinning-resolved transform. Every drawable provides local geometry bounds and an exact or conservative local hit test.

Najm distinguishes three bounds because their consumers require different semantics:

- **`GeometryBounds`** — the node's underlying local geometry before effects. Strokes may expose their own conservative expansion here or through the visual transform below.
- **`HitBounds`** — a cheap local interaction gate. By default it follows geometry and intentionally ignores glow, blur, and other visual-only effects unless a node explicitly opts in.
- **`VisualBounds`** — the conservative visible output of the node and descendants, including stroke width, fragment overlays, masks where relevant, and `EffectGraph.BoundsTransform` expansion.

Each has a corresponding subtree aggregate and invalidates upward on geometry, transform, style, effect, or child changes. The traverser resolves these aggregates through the active camera and scale-pinning frame:

- culling and isolation brackets use device-resolved **visual** bounds;
- input gating uses resolved **hit** bounds;
- the semantic `Backdrop` replacement region uses the resolved subtree geometry clipped by the active clip, not a relaxed bracket rectangle.

Unknown or deliberately unbounded Tier-3 output marks visual bounds as unknown, which disables culling and falls back to the active clip for bracket sizing. Text nodes compute geometry/hit bounds from measured logical boxes and visual bounds from ink plus active fragment overlays.

Capability access begins at attach through `Scene.Env`. Standard drawables live in `Najm.Lib`; subclassing `Drawable` and implementing `Render` remains first-class.

### 6.7 Composition model (2D)

`Node2D` carries a small declarative composition algebra. Every property applies to the node's emitted content and descendants as one compositing unit; authors do not manipulate render targets or backend brackets directly.

| Property | Meaning |
|---|---|
| `Opacity` | Group alpha of the unit |
| `Blend` | How the unit composites within its current isolation scope |
| `Clip` | Local geometric clip; clip state alone does not isolate |
| `Mask` | Secondary child collection multiplied into the unit |
| `Effect` | Image-filter graph applied after masking |
| `Backdrop` | Replaces destination pixels beneath the unit within its semantic region |
| `ZIndex` | Stable sibling paint-order key |
| `Isolate` | Forces an explicit compositing scope; default `false` |

**Semantic order:** clip → render node and children → mask → effect → replace filtered backdrop region → composite with opacity and blend. Effects are applied after masks so glow follows visible content; nesting expresses the inverse.

### Isolation and atomicity

A unit isolates when any of the following is active: non-default blend, mask, effect, backdrop, opacity below one, or `Isolate`. This rule is correct for arbitrary author-written drawables, including a childless drawable that emits many overlapping primitives.

Built-in nodes may advertise the internal capability `CompositionAtomicity.SinglePrimitive`. Only such a node may take a verified fast path that folds opacity or a compatible effect into its single paint without a bracket. Child count is never used as a proxy for atomicity. Custom drawables default to `Unknown` and therefore preserve true group semantics.

Isolation rectangles are the device-pixel-snapped intersection of resolved `VisualBounds` and the active clip, expanded by a small backend safety epsilon. Unknown/unbounded visual output falls back to the active clip. The rectangle inherits the enclosing target's color space and sample count and composites back 1:1 without resampling.

### Masks and effects

`Mask` is a secondary node collection in the owning node's local frame. Mask nodes participate in lifecycle/update but are excluded from ordinary paint and hit testing. Multiple mask children paint in order. Channel selection is `Alpha` or `Luminance`; inversion applies after extraction.

`EffectGraph` is a closed portable descriptor algebra including blur, offset, tint/color matrix, morphology, drop shadow, merge, and composition. Every operation defines a conservative bounds transform in Core. Node effect parameters are local units; layer effect parameters are virtual units. Sampling outside an isolated image uses decal transparency, and effect output may escape the content clip.

### Backdrop

`Backdrop` replaces the destination under the unit inside the resolved subtree geometry intersected with the active clip. Replacement is unconditional and is not faded by the unit's opacity. A conforming realization may sample beyond the write region; at unavailable hard boundaries it clamps edge pixels, including both edge strips and corner pixels.

### Ordering

Sibling paint order is stable by `(ZIndex, insertionIndex)`; hit testing uses the exact reverse. Update and layout retain insertion order. Isolation creates a stacking scope: descendants cannot interleave with nodes outside that scope.

### 6.8 Layout phase — contract and self-consistency rules

`Tick` runs **Input → Update → Layout** (§4.7). Layout exists for one job: computing transforms that must be resolved **after** everything else has updated — anchoring a 2D label to a projected 3D point (§8.4), keeping a callout attached to a moving widget, aligning a group after its members settled. It is a transform-resolution pass, **not** a UI layout system, a constraint solver, or flexbox (§1.3).

**Hooks and registration.** `Node.Layout(in TickContext)` and `Behavior.Layout(in TickContext)` are virtuals, but overriding is not enough: a participant **registers** at attach — `RegisterLayout(rank)` — and is unregistered at detach (§6.4). `AnchorBehavior` registers automatically. A debug-mode check warns when `Layout` is overridden but never registered. Registration (rather than call-everyone traversal) keeps the pass proportional to participants and makes ordering explicit.

**The four rules (normative):**
1. **Write-own.** A Layout hook may write **only transforms** of its own node and that node's descendants. It must not write other scene state, start coroutines, raise signals, or structurally mutate the tree. (Structural mutation attempted anyway is deferred per §6.4 and lands after the pass — legal, discouraged.)
2. **Read-settled.** A Layout hook may read any world transform, camera, or bounds — including via `TransformTo`/`ToLocalOf` (§6.3). Reads of transforms that are themselves layout-written are **fresh only if the writer is ordered earlier** (rule 3); otherwise they observe last frame's value. Depending on an unordered read is the bug this contract exists to prevent.
3. **Rank ordering.** Participants execute in ascending **rank** (int, default 0), ties broken by tree order — deterministic (§6.5). `AnchorBehavior` derives its rank automatically as *(the maximum layout rank among participants writing its target's transform chain) + 1*, so anchor→anchor chains resolve in one frame with zero configuration. Rank derivation that cycles (A anchors into B's write-set and vice versa) **fails fast at attach** with a named error. Custom participants that read layout-written transforms declare a higher rank the same way (helper provided) or set it manually.
4. **Idempotence.** Given identical pre-layout state, a Layout hook produces identical writes: no accumulation, no RNG, no time-integration. Layout is a pure resolution pass, re-runnable in principle. (Integration belongs in `Update`.)

**Completeness note (deliberate one-frame lag):** non-transform state that depends on a layout-resolved transform — e.g., a readout label's *text* showing an anchored position — is written in Update and therefore observes **last frame's** resolution. This is a procedural engine, not a UI engine; the lag is accepted. Where a single exported still must be exact, compute the projection yourself in Update.

**Arrangement helpers** (`Najm.Lib`, planned): `NextTo`, `AlignTo`, `Distribute`, `Arrange` — pure position-computing functions over nodes/bounds, usable from `OnLoad`, `Update`, coroutines, *or* a Layout hook, with **camera-aware overloads for pinned targets** (§6.3). They are functions, not a layout engine: calling `label.NextTo(circle, Side.Right, gap: 8)` once in `OnLoad` places a static label; wrapping the same call in a registered Layout hook makes it track. This is the sanctioned answer to relative positioning in a labeling-heavy domain.

---

## 7. 2D draw abstraction

The heart of "backend-agnostic but Skia-powerful." Four tiers: core, convenience, **bulk**, and backend power.

### 7.1 Tier 1 — core primitives (every backend MUST implement)

- **Filled/stroked `Path`** — the universal primitive, with **`FillRule { NonZero, EvenOdd }`** (default `NonZero`; even-odd is how holes, rings, and difference-style shapes are cut without boolean path ops). `PathBuilder` produces backend-agnostic geometry (verbs: move/line/quad/cubic/arc/close; carries its fill rule; resettable/reusable).
- **Text run** — `DrawText(ITextLayout layout[, in PathPlacement placement][, ReadOnlySpan<FragmentOverlay> overlays][, Color? colorOverride])`: Tier-1 **abstract, with no portable default** — §12.1 forbids a parallel text rasterizer, and there is nothing portable to rasterize glyphs with; each backend lowers natively (Skia: NAJM-SKIA II.3).
- **Image blit with an affine matrix.**

### 7.2 Tier 2 — convenience primitives (portable defaults, optional overrides)

Implemented **once** in abstract `DrawContext2DBase` in terms of Tier 1, so every backend gets them correct for free; a backend MAY override any of them for quality/speed:

`Circle`, `Ellipse`, `Rect`, `RoundRect`, `Line`, `Polyline`, `Arc`, and **`DrawImageQuad(image, c0..c3)`** — portable default: subdivided affine mapping (grid subdivision approximates perspective arbitrarily well); Skia override: true perspective via `SKMatrix` homography terms. `DrawImageQuad` exists now because the scene-in-3D seam (§14.3) lands on it.

`SkiaDrawContext2D` overrides e.g. `Circle → SKCanvas.DrawCircle` for perfect AA. *This is the settled answer to "how is a circle backend-agnostic": convenience + portable default + optional override.*

### 7.3 Tier 2.5 — bulk primitives (2D batches)

Dense 2D data — scatters, quivers, particle fields, sprite clouds — is as central to the mission as dense 3D data (§1.4), and it is also the **flush target of the 3D projection backend** (§8.3), so bulk drawing is part of the portable 2D contract, not a Skia extra:

```csharp
void DrawPoints (in PointBatch2D b); // positions[]; size (local units); one paint,
 // or per-point Colors[], or Scalars[] + TransferFunction
void DrawLines (in LineBatch2D b); // segment endpoint pairs[]; width (local units);
 // one paint or per-segment colors
void DrawSprites(in SpriteBatch2D b); // IImage + per-sprite pos/rot/scale (RSXform-like);
 // optional per-sprite color/alpha modulation
```

- Batches are **spans over pooled or author-owned arrays** (`ReadOnlySpan`-friendly) — one managed call for 10⁵ elements.
- **Sizes are local units** (§7.4); a batch that must hold constant virtual footprint divides by `ctx.Scale` once per batch — the plot-component idiom of §6.3, one division, not per-element cost.
- **Portable default** (in `DrawContext2DBase`): a loop over Tier 1/2 — correct everywhere, including vector contexts, where the loop *is* the desired path emission.
- **Skia overrides**: `SKCanvas.DrawPoints` / `DrawAtlas` / `DrawVertices`, chosen per batch shape — the canonical fast paths, never per-element managed calls.
- **Compose:** 2D batches default to source-over in array order; the **order-independent** compose modes `Additive` and `MaxPerChannel` (§8.2) are accepted for density-style rendering (additive scatter glow, per-channel max heat). Depth-dependent modes are meaningless in 2D and rejected.
- `Najm.Lib` wraps the common cases as nodes (`ScatterNode`, `Quiver2DNode`), each typically one batch call (§6.6), and gives batch nodes a lightweight spatial index with **`PickElement(Vector2 local) → int?`** — element-level picking (hover a scatter point) without per-element nodes; the node stays one draw call, `IInteractive` (§9.3) supplies the pointer, `PickElement` supplies the datum.

### 7.4 Descriptors, handles, and units (no backend types in public API)

- **`Paint`** — a plain **value** descriptor: color or brush, fill/stroke style, stroke width/cap/join/miter, dash, AA (default on), and a blend mode from the portable subset below. Factories: `Paint.Fill(color)`, `Paint.Stroke(color, width, ...)`.
 **Implementation rule:** the Skia context stamps `Paint` fields onto **pooled `SKPaint`** objects — never allocate `SKPaint` per call (the canonical SkiaSharp perf/leak trap).
- **Portable blend subset (enumerated, closed):** `SrcOver` (default), `Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`, `ColorDodge`, `ColorBurn`, `HardLight`, `SoftLight`, `Difference`, `Exclusion` — the separable modes SVG and PDF both express, so they survive vector export — plus **`Plus`** (additive), which is **raster-only** and degrades under `VectorTarget` per §7.6. This one list serves `Paint`, node `Blend` (§6.7), and layer `Blend` (§5.2); full Skia blend modes remain Tier-3 territory (§7.5). Fidelity per format: the NAJM-SKIA table.
- **All sizes are local units.** Stroke widths, dash intervals, batch footprints, text sizes, and effect parameters are expressed in the node's local coordinates and scale with the accumulated transform. There is no per-quantity `SizeSpace` property. Camera-invariance is the node-level `ScaleMode` (§6.3); the per-quantity escape is the **`ctx.Scale` idiom**: the context exposes read-only **`RenderScale`** (§5.1) and **`Scale`** — the accumulated author→virtual scale at the current transform, defined under non-uniform transforms as `sqrt(|det M₂ₓ₂|)`, i.e. the geometric mean of the polar singular values (consistent with §6.3's decomposition). `width: 3f / ctx.Scale` is a constant-virtual stroke on world geometry; `Najm.Lib`'s plot components encapsulate it so plot users never see it.
- **`Brush`** portable subset: solid, linear/radial gradient, image pattern.
- **Handles** via `IAssets`: `IImage` (incl. `CopyPixels`, §4.4), `FontFace`, `IShader`, **`IPath`** (a `PathBuilder` baked once, fill rule included — static shapes bake at attach and draw by handle; dynamic geometry uses `DrawPath(PathBuilder)` per frame). Native realizations live in backend-owned side tables per §4.4.
- Minimal context state ops: `PushClip(rect | IPath, FillRule)` / `PopClip`; `PushOpacity(a)` / `PopOpacity` (Skia: `SaveLayer` alpha when < 1; portable default: alpha modulation); **`PushTransform(Matrix3x2)` / `PopTransform`** — *local, compositional* transforms for drawables that paint repeated or nested sub-geometry (a gear's teeth, a fractal stage) without managing matrices by hand. The **node→world** transform remains engine-owned and is carried by the context internally; `PushTransform` composes strictly below it and must be balanced within the `Render` call (debug-asserted).

### 7.5 Tier 3 — backend power (per-drawable opt-in with attach-time safety)

`SkiaDrawContext2D` **additionally** exposes Skia-only constructs: the raw `SKCanvas`, pooled `SKPaint` escape, **runtime SkSL** (`SKRuntimeEffect`), image filters, path effects, full blend modes.

```csharp
public abstract class Drawable : Node2D
{ // portable — runs on any backend
 public abstract override void Render(IDrawContext2D ctx);
}

public abstract class SkiaDrawable : Node2D
{ // full Skia power, opt-in
 public sealed override void Render(IDrawContext2D ctx)
 => RenderSkia((SkiaDrawContext2D)ctx); // guaranteed by attach-time check
 protected abstract void RenderSkia(SkiaDrawContext2D ctx);
}
```

A `SkiaDrawable` validates `env.Caps.SkiaSurface` at attach and **fails fast** on a non-Skia target — a configuration error, not a runtime surprise. Authors mix freely: portable drawables for portability, `SkiaDrawable` for creative fiddling; per-drawable, opt-in lock-in. Since the project is primarily Skia, this tier is first-class, not an afterthought.

**External GPU interop (documented author pattern).** `Najm.Skia` exposes wrapping an externally rendered GL texture as an `IImage` (`SkiaInterop.WrapGlTexture`-style), so a custom GL/Vulkan pipeline is *an ordinary drawable that owns its render-to-texture privately* — the §8.5 seam generalized to authors. Rules: the texture must come from a context shared with the host's `GRContext`; the author flushes/fences their GPU work before the wrap is sampled; the wrapped image obeys the snapshot-validity invariant (§5.3). Realization detail: NAJM-SKIA.

**Layers are non-generic.** Backend differences live in draw contexts and surface providers; layer types describe coordinate spaces and layer-level behavior.

### 7.6 Export contexts, vector policy — the honest second implementation

Skia's SVG and PDF canvases *are* `SKCanvas`es, so the export contexts are **the same `SkiaDrawContext2D`** pointed at a vector canvas, with `Caps.VectorTarget` set (SkSL/filters rasterize). The export backend is configuration, not code — and it doubles as the second `IDrawContext2D` implementation for the convenience/bulk-parity test (§18).

**Vector fidelity, stated concretely:** PDF's blend-mode set includes `Lighten` (so `MaxPerChannel` survives as vector) but has **no additive `Plus`** — `Additive` content in a PDF export rasterizes or mis-renders; SVG blend support is spottier still. And even fully-representable dense content (a 40k-primitive point cloud) produces vector files that choke viewers. Both problems have the same answer:

**`VectorPolicy { Auto, Vector, Raster }`** — per-instance control over how a subtree or batch lands in vector output:
- A **property** on nodes (`node.VectorExport`) and a **field** on batches — instance-level, because density is a per-figure fact: the same `PointCloudNode` class is sparse in figure 2 and 40k-dense in figure 5.
- A **class-level default** via the `[VectorExport(VectorPolicy.Raster)]` attribute on `Drawable` subclasses, for things dense by nature; read once at attach. Precedence: instance property → class attribute → `Auto`.
- `Auto` = vector; the initial exporter uses no heuristics. `Raster` = the exporter renders that node's subtree (or batch) into an offscreen raster at the export's `RenderScale` (or an explicit per-node scale) and embeds the image — matplotlib's `rasterized=True`, generalized. The result: publication figures with vector axes, curves, and text around an embedded raster cloud; correct additive glow included.
- **Composition features under `VectorTarget`** (summary; per-format detail in the NAJM-SKIA fidelity table): clips, group opacity, and the portable blend subset survive as vector; masks export where the backend expresses them (SVG `<mask>`, PDF soft masks); `Effect`/`Backdrop` graphs and `ReadsBackdrop` degrade per the normative pipelines below; `Plus` rasterizes.
- Under `VectorTarget`, features outside the policy's reach degrade as documented below; the parity test (§18) covers Tier 1–2.5 behavior, not Tier 3.

**Degradation pipelines (normative — full realization NAJM-SKIA Part III).** Degradation behavior is author-observable, so the architecture defines *what happens* while the fidelity table identifies *where each format requires it*:

- **`ReadsBackdrop` under the direct path:** layers below the RB layer emit as vector normally; at the RB boundary the direct path **re-renders the below-stack to a raster target** at the export's raster scale (a fresh walk — licensed by render idempotence, §4.1), initializes the RB layer's raster from it, renders the RB layer's tree to raster, lerp-merges per §5.3, and **embeds the RB layer's region as an image** in the vector output; layers above continue as vector on top.
- **Node-tier `Backdrop` under the direct path:** the traverser re-renders the destination-so-far *within the layer* (a walk from the layer root, stopping before the unit), clipped to the resolved subtree-geometry region and sampled over the graph's required outset (§6.7), into a raster target; the region embeds as an image drawn through the lowered graph with `Src`; the unit then composites as vector above it. Nested in-layer backdrops re-walk per occurrence (quadratic, accepted for figure workloads).
- **Glyphs export as outlines (paths) by default** — publication output must not depend on viewer-installed fonts. Mechanized: glyph runs emit as filled outline paths via a per-face glyph→path cache; blobs are never emitted to vector canvases (route-asserted). **Bitmap/COLR/emoji glyphs have no outlines — the affected run rasterizes** (the unit-rasterize failure mode; never a silent drop; the offending face is flagged and named). **`VectorTextPolicy { Outlines (default), Embed }`** is a deferred option, **riding `VectorExportOptions`**; the typesetter seam supplies the glyph→font-data mapping when `Embed` lands. **`ITextLayout.BakePath → IPath`** (all glyph contours, `FillRule.NonZero`) serves text-shaped `Clip`s, masks, and morph sources — overlap caveat documented: nonzero fills overlaps solid; even-odd consumers beware.
- **Unexpressible blends and masks rasterize the affected unit, never silently downgrade to `SrcOver`** — the failure mode is a correct raster region, not a wrong vector one. Per-format expressibility lives in the NAJM-SKIA fidelity table; `VectorPolicy.Raster` remains the author's blunt override.

Idempotence (§4.1) is exactly what makes the re-walk pipelines legal — re-walking is re-rendering, and re-rendering is free of observable effect by contract: this is that contract earning its keep.

---

## 8. 3D pipeline

**The 3D contract is defined as rendering semantics — geometry, projection, and compositing mathematics — never as Skia capabilities.** Each backend realizes the semantics with documented fidelity; Skia blend modes are an implementation convenience for the CPU backend where they happen to coincide with the definition, and nothing more (§1.2 pillar 4).

### 8.1 Nodes and cameras

`Node3D` family + `Camera3D` (a `Node3D` — cameras are nodes in both spaces, §5.2). The camera carries an explicit **`ProjectionMode { Perspective, Orthographic }`** (`Fov` vs. `OrthoHeight`; shared `Near`/`Far`) and assembles `View`/`Projection` (`Matrix4x4`) in **overridable** `BuildView` / `BuildProjection` — matrix-level custom projections (oblique, off-axis, asymmetric frusta) are a camera subclass away, optionally paired with a `WorldLayer3D` subclass (§5.4). *Nonlinear* projections (fisheye, equirectangular) bend line primitives and are outside the vector pipeline's vocabulary — GPU-backend territory. `WorldToVirtual` / `VirtualPointToRay` as per §3.3. Cameras are **nodes** — parentable, animatable, multiple per scene; a layer *references* one (§5.2, §14.4). **Sugar:** assigning a parentless camera to a layer auto-attaches it to the layer root (kills the make/attach/assign three-step). `Najm.Lib` ships `OrbitCameraBehavior` and `FlyCameraBehavior` (the hydrogen-cloud demo is the fly-camera test case).

### 8.2 IDrawContext3D — the vocabulary

Engine-native (`Vector3`, `Color`, sizes in **virtual units by the 3D contract's own definition** — a rule this contract owns, independent of 2D sizing §7.4): points, line segments, polylines, flat polygons/triangles, billboard text, **`ImageQuad`** (four 3D corners + `IImage` — sprites, and the scene-in-3D seam §14.3), plus **batch primitives**:

```csharp
ctx.DrawPoints(new PointBatch(positions, size, colorsOrScalars, compose, depthCue));
ctx.DrawLines (new LineBatch (segments, width, paint, compose, depthCue));
```

A batch = position array (`ReadOnlySpan`-friendly) + footprint parameters + **element appearance** — either direct `Colors[]` / one paint, or **`Scalars[]` + a `TransferFunction`** (scalar → color/α, a first-class helper) — + a compose mode + a coarse depth key + optional `DepthCue`. Batches are the only sane path to 10⁴–10⁵ elements (§1.4): one node, one call — never per-element managed calls. Helpers: parametric curve, parametric surface wireframe, axes/grid.

**Per-batch compose modes (`BatchCompose`) — ray-integration semantics, defined mathematically:** every batch fixes how its elements integrate along the view direction. These are compositing semantics, not camera math, and their definitions are backend-independent:

- **`SortedAlpha`** (default) — back-to-front source-over: the discretization of **emission–absorption** over point-sampled data (with `TransferFunction` supplying per-element emission and opacity).
- **`Additive`** — pure energy accumulation: the pixel is the **sum** of contributions. Order-independent.
- **`Max`** — **true maximum-intensity projection (MIP)**: the pixel shows the single element of **maximal scalar intensity** along the ray — argmax on `Scalars`, keeping *that element's* color. Requires `Scalars`; a `Max` batch without scalars **fails fast**. Order-independent in result, though realizations may sort (§8.3).
- **`MaxPerChannel`** — per-channel color maximum. Order-independent and cheap; **exact** for monochromatic transfer ramps (scalar × single hue), where it coincides with `Max`; for polychrome data it hue-shifts and is offered as the explicitly-named fast approximation, never silently substituted for `Max`.

**`SplatBatch`.** Camera-facing soft splats: positions + radius (isotropic fast path) or per-splat anisotropic footprint (Gaussian-splat style: scale/rotation or 2D covariance) + colors or scalars+transfer, combined with any compose mode. Point-sampled fields, orbital clouds, and pre-trained Gaussian splats all land here.

**Field-based volume rendering** (ray-marched DVR over 3D scalar fields) needs per-fragment integration and is **GPU-backend scope** (§8.5); the initial 3D path reaches the same visual goals through point-sampled representations + compose modes. (A CPU ray-marcher emitting an `IImage` onto an `ImageQuad` remains ordinary author-side node code for offline stills — not engine surface.)

### 8.3 ProjectionDrawContext3D (first backend — in Core, backend-agnostic)

The public API stays immediate-mode; this backend **reifies**: collect primitives into **pooled** internal structs (point/segment/triangle/quad/batch + appearance + depth key) during traversal → **sort** as the compose mode requires → project through `Camera3D` → **flush as 2D emission through the Tier 1–2.5 surface of any `IDrawContext2D`**: sparse primitives through Tiers 1–2, batches through the **bulk tier (§7.3)** — points via `DrawPoints`/`DrawSprites`, splats via `DrawSprites` (which Skia realizes as `DrawAtlas` for isotropic and textured `DrawVertices` for anisotropic footprints), segments via `DrawLines`. Never per-element managed calls; the portable bulk defaults keep even non-Skia targets legal. Consequences: identical minimalist vector look to 2D, trivial compositing, all of Skia's AA/stroke quality reused — and because the flush is path emission, **3D scenes export as vector PDF/SVG** (§1.1.3), a genuinely rare capability (subject to `VectorPolicy`, §7.6). Reified structs are internal, never public API.

**Compose-mode realizations (this backend):**
- `SortedAlpha` → stable **depth sort** (per primitive; *within-batch* sort + coarse batch key against other geometry — approximate, fine for clouds) → source-over.
- `Additive` → no sort; Skia `Plus`.
- `Max` (**true MIP**) → stable **ascending-intensity sort** on `Scalars` → **opaque overwrite** (source-over at full alpha): last-writer-wins per pixel *is* argmax. **Exact for hard footprints**; soft splat edges still alpha-blend, making boundaries approximate — documented fidelity, with the GPU backend (§8.5) evaluating exactly per fragment. The sort machinery is shared with `SortedAlpha` (different key).
- `MaxPerChannel` → no sort; Skia `Lighten` (per-channel max).
Under `VectorTarget` caps, `Additive` degrades and `MaxPerChannel` survives as noted in §7.6; `VectorPolicy.Raster` is the sanctioned fix for dense or additive figure content.

**Painter's-algorithm limits (documented scope, resist scope creep):** per-primitive sorting cannot resolve intersecting triangles or ordering cycles. This backend targets **sparse vector 3D** — curves, wireframes, point clouds, small meshes — which is exactly the minimalist aesthetic. It is not, and must not grow into, a rasterizer. Compose modes widen what *point-sampled* data can express (MIP, emission–absorption, additive glow) without changing this scope — surfaces still sort per primitive.

**`DepthCue`** helper (e.g., `DepthCue.Fade(0.35f)`, thin-by-depth): one multiply turns a flat cloud volumetric, and it vectorizes in figure export.

### 8.4 3D drawables & interop (Najm.Lib)

`PointCloudNode`, `Line3DNode`/`Polyline3DNode`, `MeshNode` (small), `SurfaceNode` (wireframe), `AxesNode`/`GridNode` — each typically one batch call. **2D↔3D anchoring:** an `AnchorBehavior` reads `Camera3D.WorldToVirtual(target.WorldPosition)` in the **Layout** phase (§6.8 — after Update settles cameras; auto-registered, auto-ranked) and positions a 2D annotation — the canonical "label tracks a 3D atom" tool, enabled by always-available world transforms (§6.3) and unaffected by scale pinning (`WorldPosition` is exact for pinned annotation nodes, §6.3).

### 8.5 Future seam: GpuDrawContext3D (Najm.GL3D)

Same `IDrawContext3D`, implemented by a future **`Najm.GL3D`** backend (its own companion document, NAJM-GL3D — §1.5): real depth buffer, MSAA, instancing for 10⁵⁺ elements, native splat rasterization, and **ray-marched volume rendering over scalar fields** — the same `BatchCompose` vocabulary evaluated **exactly, per fragment along rays**: per-fragment argmax for `Max`, integrated emission–absorption for `SortedAlpha` semantics, summation for `Additive`. It renders to texture **privately** and hands the compositor a texture-backed `SKImage`; NDC/depth conventions convert inside it, in one place (§3.2). No scene-script changes — `IDrawContext3D` is **locked at M4 including `BatchCompose`, `Scalars`/`TransferFunction`, and `SplatBatch` semantics**, so both backends implement one mathematical contract.

---

## 9. Input

### 9.1 InputBlock (per-frame pooled data, virtual coordinates)

- **Events:** pointer (unified mouse/touch with a **pointer id** — near-free now, needed for web), keyboard down/up, **text input (rune) events** — required for `TextBox` and the sorting-demo array entry; key codes alone are the classic trap — and scroll.
- **Snapshots** for the polling API: pointer position/buttons, key states.
- Hosts translate platform events and **inverse-letterbox** pointer coordinates into virtual space before the scene ever sees them. **Pointer coordinates outside the letterbox map linearly and are delivered unclamped** — virtual coordinates may be negative or exceed `VirtualResolution`; authors wanting containment test bounds themselves (off-canvas drags stay smooth by default).
- **Host-reserved keys:** the overlay toggle (default `F1`) and the manual-restart key (default `F5`, §15) are consumed by the host and never appear in the `InputBlock`; both rebindable via `HostOptions`.
- Consumption is tracked alongside events; `Update`-phase polling sees **unconsumed** input only.
- **Deterministic runs carry the canonical empty block** (§2.1): no events, default snapshots; the router idles; polling reads defaults. Scenes intended for deterministic runs do not consult input (§2.5, Appendix A.1).

### 9.2 Router

The router runs per scene during the Input phase. Captured and focused targets are dispatched first; otherwise each input-participating layer is visited top-to-bottom and its node tree in exact reverse paint order.

Hit testing is camera- and pinning-resolved. The router never treats camera-free `InverseWorld` as sufficient for a pinned node:

```csharp
ResolvedNodeFrame frame = layer.Resolve(node, camera);
if (!frame.HitBoundsVirtual.Contains(pointerVirtual))
 continue;

Vector2 local = frame.VirtualToLocal(pointerVirtual);
if (node.HitTest(local))
 return node;
```

`ResolvedNodeFrame` contains local↔virtual transforms and resolved hit/visual bounds for the current layer, camera, viewport, and scale mode. `Clip` gates the walk. Masks and effects do not affect hit testing unless a drawable explicitly includes them in `HitBounds`. Disabled or invisible ancestors make descendants ineligible. Pointer capture and keyboard focus bypass the walk until released; detach releases both deterministically.

### 9.3 IInteractive (opt-in — for any 2D node, not only widgets)

`OnPointerEnter/Exit/Down/Up/Move`, `OnDrag`, `OnScroll`, `OnFocus/Blur`, `OnKey`/`OnTextInput` (when focused), plus `HitTest`/bounds from the drawable contract. **`IInteractive` is valid on any node in any 2D layer** — a draggable point in a `WorldLayer2D` is as first-class as a `Slider` in the `ScreenLayer`; the router's retained-transform conversion makes world-space interactives work identically. Polling remains available as the lightweight alternative — hybrid routing by design — but polling cannot participate in capture or consumption; anything drag-shaped belongs on the router.

**Event coordinate spaces are explicit.** Pointer events deliver **both** spaces, computed at dispatch:

```csharp
public readonly struct PointerArgs
{
 public Vector2 Virtual { get; } // scene virtual coords (§3.3)
 public Vector2 Local { get; } // the receiving node's local space
 public int PointerId { get; }
 public PointerButton Button { get; }
 // + modifiers, scroll delta where applicable
}
```

**Dragging is a shipped mechanism, not a per-demo reinvention:** `Najm.Lib` provides `DraggableBehavior` (router-backed: correct capture, drag deltas in local/world/virtual, grab-offset handling, optional axis/region constraints) and a styled-free `DragHandleNode` (ships **pinned** — constant virtual size under zoom, §6.3). Dragging a point on a curve is *the* fundamental gesture of interactive math; it costs one attach. Element-level picking inside batch nodes is `PickElement` (§7.3).

### 9.4 Deferred

3D picking beyond `VirtualPointToRay` (the API exists; a router integration waits for a demo that needs it — the documented recipe: ray → plane/primitive intersect → local/UV, per §14.3); IME/complex text (see §12 roadmap); gamepads; web input mapping. Keyboard is **first-class native** now.

---
## 10. Coroutines, waits, signals, animation

The scripting workhorse. Choreography that reads as sequence is written as sequence. This section is the **normative semantics contract** — every rule here is testable and tested (§18). For when to reach for a coroutine versus a behavior versus a tween, see the decision rule in §6.2.

### 10.1 Form and vocabulary

```csharp
IEnumerator<Wait> Intro()
{
 yield return Wait.For(circle.FadeIn(0.5)); // join an animation
 yield return Wait.Seconds(0.25);
 yield return Wait.All(axis.SlideIn(...), grid.FadeIn(...));
 label.Text = "Fourier";
 yield return Wait.Signal(NextBeat); // presentation stepping
}
Start(Intro()); // scene-lifetime
node.Start(Intro()); // node-lifetime (auto-cancelled on detach)
```

`Wait` is a **struct** (never boxed; pooled where it carries payload). Vocabulary:

| Wait | Meaning |
|---|---|
| `Wait.NextFrame` | resume in the next frame's pass |
| `Wait.Frames(n)` | resume after `n ≥ 1` passes (`NextFrame ≡ Frames(1)`) |
| `Wait.Seconds(s)` | resume on the first pass where accumulated sim-time ≥ `s` |
| `Wait.Until(pred)` | resume on the first pass where `pred` is true |
| `Wait.Never` | never resumes (cancel to end; idiomatic for "park until stepped/cancelled") |
| `Wait.For(handle)` | resume when a `CoroutineHandle`/`AnimationHandle` finishes (any terminal status) |
| `Wait.For(routine)` | sugar: starts the child routine (same owner), then `For` its handle |
| `Wait.All(...)` | resume when **all** constituents have finished |
| `Wait.Any(...)` | resume when **any** constituent finishes; the others are unaffected |
| `Wait.Signal(sig)` | resume when the signal is raised (§10.5) |

### 10.2 Scheduler contract

1. **One pass per frame.** Coroutines resume during `Update`, **after** the full tree update (nodes, behaviors, layers) and **before** the Layout pass (§4.7, §6.8). So a resumed routine observes this frame's settled Update state, and anything it writes is seen by this frame's Layout and Render.
2. **The tween pass precedes the coroutine pass** (§4.7). All live `AnimationHandle`s advance by `Dt` immediately before coroutines are drained, so a tween reaching its end at frame N releases its `Wait.For` waiters **in the same frame's pass** — chained animations are gap-free, and their durations sum exactly: 0.5 s + 0.5 s at fixed 60 fps occupies exactly 60 ticks (tested, §18).
3. **FIFO drain.** The pass drains a queue in enqueue order. Routines started *before* the pass (in `OnLoad`, `OnStart`, tree `Update`, input handlers) are already queued and get their first resume **this frame**. Routines started *during* the pass — e.g., a child started by `Wait.For(Nested)` — are **appended and resumed later in the same pass** (the queue drains to empty). This makes nesting compose without a one-frame hiccup and keeps order deterministic (enqueue order). A routine that spawns unboundedly during the pass is an author bug (infinite loop), not a scheduler ambiguity.
4. **Resume = evaluate-then-advance.** At each pass, a live routine's current `Wait` is evaluated (lazily — see Pause, §10.4); if satisfied, the routine runs to its next `yield` (or completion).
5. **Eligibility.** A routine or tween owned by a node whose effective `Enabled` is `false` (self or any ancestor) is **ineligible** — exactly `Pause` semantics (§6.1, §10.4): its wait is not evaluated, `Seconds` and tween time stop accumulating, `Frames` doesn't count. Re-enabling resumes in place.
6. **Phase legality.** Starting coroutines from `Render` violates render idempotence (§4.1); from `Layout`, the layout rules (§6.8 rule 1). Both are contract violations (debug-asserted). Input handlers and `Update` are the normal starting places.
7. **Sim time only.** `Wait.Seconds` and tweens consume `tick.Time.Dt` — never wall clocks — so live/fixed/retimed (`SceneNode` slow-mo) behavior is uniform and deterministic runs are exact.

### 10.3 Per-wait semantics (normative)

- **`Frames(n)`** counts scheduler passes in which the routine was eligible (not paused, not owner-disabled).
- **`Seconds(s)`**: the wait accumulates `Dt` at each eligible pass and releases on the first pass where the accumulation ≥ `s`. **No fractional remainder carries** into subsequent waits: at fixed 60 fps, `Seconds(0.5)` is exactly 30 ticks (§2.3), and chained `Seconds` quantize per-wait to the tick grid. (Deterministic either way; the simple rule wins. Authors needing exact long-horizon schedules key off `Time.Elapsed` with `Until`.)
- **`Until(pred)`**: `pred` is evaluated **once per eligible pass**, during the pass — treat it as a pure read of scene state.
- **`For(handle)`** releases when the target reaches **any terminal status** — `Completed`, `Cancelled`, or `Faulted` (§10.4). The waiter resumes normally and may branch on `handle.Status`; a child's cancellation or fault never silently kills the parent.
- **`All`** releases when every constituent is terminal. **`Any`** releases when the first constituent turns terminal; **the unfinished constituents keep running** — they are independent; cancel them explicitly via their handles if losers should stop. (`Any(Wait.Signal(skip), Wait.Seconds(5))` — the idiom for "auto-advance unless skipped" — needs no cleanup: bare condition waits own nothing.)
- **`Signal(sig)`**: releases if `sig` is latched this frame (§10.5), including when the raise happens **during the pass** before this waiter is evaluated (drain order, §10.2.3).

### 10.4 Handles: status, pause, cancel, step, exceptions

```csharp
public enum RoutineStatus { Running, Completed, Cancelled, Faulted }
CoroutineHandle: Status, IsRunning, Pause, Resume, Cancel, bool Step
```

- **Ownership.** `Scene.Start` / `Scene.Animate` = scene-lifetime (cancelled at `OnStop`); `node.Start` / `node.Animate` and node property helpers = node-lifetime (cancelled at detach, during the deferred flush, §6.4 — tweens stop **at current value**, §10.6). Owner-disabled = ineligible (§10.2.5).
- **`Pause`** removes the routine from eligibility: its `Wait` is **not evaluated** — `Seconds` stops accumulating, `Until` isn't called, `Frames` doesn't count. Pausing freezes the routine's subjective time; `Resume` continues where it left off.
- **`Cancel`** is immediate and synchronous: the routine will never resume; `enumerator.Dispose` runs **at the call site**, so `try/finally` cleanup executes deterministically (this is the contract that makes `finally`-based cleanup reliable — Appendix A.1); `Status = Cancelled`; `For`-waiters release at their next evaluation. Detach-cancellation behaves identically at flush time.
- **`Step`** — synchronous single-step for algorithm walkthroughs and debugging; works on paused routines; intended between frames (host/tooling/driver code), legal from `Update`. Semantics: **fast-forward the current wait, then resume exactly once.**

 | Current wait | Under `Step` |
 |---|---|
 | `NextFrame` / `Frames` / `Seconds` / `Until` / `Never` | deemed satisfied (remaining time/frames skipped; sim clock untouched; `pred` not called) |
 | `Signal` | **bypassed** — the signal is neither raised nor consumed; other waiters are unaffected |
 | `For(AnimationHandle)` | the animation is **`Complete`d** (jump-to-end, §10.6) |
 | `For(CoroutineHandle)` | the **join is released**; the child routine is *not* force-run and continues on its own schedule |
 | `All` / `Any` | each constituent treated per this table / released outright |

 `Step` drives **only the stepped routine** — it never executes other routines synchronously (no unbounded cascades, no reentrancy surprises). If the routine was waiting on nothing yet (never resumed), `Step` performs the first resume. Returns `false` iff the routine was already terminal. A stepped routine remains paused if it was paused (step-through debugging composes with `Pause`).
- **Exceptions.** A throw during resume marks the routine `Faulted`, disposes the enumerator (`finally` runs), and **rethrows to the driver** — fail loud (§2.2 discipline; a presentation host that must survive scene bugs catches at its tick boundary). `For`-waiters observe `Faulted` and may branch.

### 10.5 Signals — the replayable stimulus

```csharp
public sealed class Signal { public void Raise(); } // + Signal<T> with a payload
```

- **Frame-latch semantics:** `Raise` latches the signal **for the remainder of the current frame**; every `Wait.Signal` evaluated during that frame's pass releases (whether the waiter enqueued before or after the raise — no missed-signal race between the Input phase and the coroutine pass, and none between routines in one pass). The latch clears at frame end; multiple raises in one frame are one latch. `Signal<T>` latches the **last** payload of the frame.
- Signals are the **only sanctioned external stimulus in deterministic runs** (§2.1): driver-raised (presentation "next beat", tooling), loggable as `(frame, signal)` pairs, and **replayed by re-raising at recorded frames** (§2.2). User input, by contract, does not exist there.
- Waits + signals + handles deliberately stop short of an async/await dataflow framework: this is frame scripting, not concurrency. There is likewise **no engine-level event bus or signal-propagation system** — cross-cutting fan-out, where genuinely needed, is an author-owned hub class registered as an Ambient (`Najm.Guard` may ship one).

### 10.6 Tweens (the micro layer)

```csharp
AnimationHandle h = Animate(v => circle.Radius = v, from: 0, to: 40,
 duration: 0.6, ease: Ease.OutCubic);
yield return Wait.For(h);
```

Small, allocation-conscious, driven by the same sim clock in the **tween pass** (§10.2.2). `Animate` applies the from-value **synchronously at the call site**; the first `Dt` is consumed at the next tween pass. `AnimationHandle`: `Status`, `Pause/Resume`, `Cancel` (stop **at current value** — also the detach behavior for node-owned tweens, §10.4), and **`Complete`** (jump to the end: the setter is invoked once with the final value, `Status = Completed` — the primitive `Step` relies on, and the idiom for "skip the transition, keep the result"). Property helpers (`node.FadeIn(0.5)`, `MoveTo`, `ScaleTo`) return handles, are node-owned, and compose with `Wait.All/Any`. A serious easing library (`Ease`) ships in `Najm.Utils` — quality of motion is a pillar. Tween tracks stay dumb; sequencing lives in coroutines. **No reactive/animatable-property system** — properties are plain fields/setters written imperatively.

---

## 11. Audio

Scenes **emit** audio; hosts **realize** it — audio is data, like drawing:

```csharp
env.Audio.Play(clipHandle, at: tick.Time.Elapsed, gain: 0.8f);
```

- **Live:** `DeviceAudioSink` (OpenAL behind the seam, per §16) plays immediately.
- **Deterministic:** `CueRecorder` writes `(t, clip, params)` cue lists (JSON) — offline "audio" is a cue log for editor muxing; frame-exact video/audio sync at zero DSP cost (§1.1.1 — the editor owns final assembly).
- **`TeeSink(a, b)`** duplicates emissions — the composition that gives live capture (§4.6) its cue sidecar (device + recorder simultaneously), and embedders their mixing/muting seam (§14.2).
- Deferred (seams reserved): mixing graph, DSP, spatialization, streaming synthesis. `IAudioClip` handles come from `IAssets` like any asset.

---

## 12. Typesetting

### 12.1 Authority and portable model

All text — labels, paragraphs, mathematics, readouts, and text-on-path — is produced by the environment's `ITypesetter` and drawn through the Tier-1 `DrawText` operation. Nodes and draw contexts never shape text.

Core owns portable types: `FontFace`, `FontFamily`, `Style`, `RichContent`, `TypesetRequest`, `ITextLayout`, glyph/rule/vector-picture runs, fragments, and path-placement data. `Najm.Text` produces layouts. Backends own native side tables for typefaces, blobs, glyph paths, and picture realizations; no shared handle contains a mutable backend `object` slot.

### 12.2 Initial scope

The first usable text slice includes pinned Latin Modern defaults, HarfBuzz shaping for the Latin-oriented baseline, measured logical/ink bounds, baseline anchors, plain `TextNode`, `TexNode`, dynamic numeric readouts, and outlined SVG/PDF export. Rich markup, full BiDi/fallback, selection/IME, and external TeX remain staged in `ROADMAP.md` and `NAJM-TEXT.md`.

### 12.3 Node semantics

Text size is a typesetting input. Animate node scale for continuous size animation; changing size or content is a measured transition that may allocate and rebuild layout caches. Anchoring is a node-side offset over an immutable layout. The upright rule uses the owning layer's y-axis orientation so glyphs remain visually upright in Y-up worlds.

### 12.4 Rich text and fragments

Markup syntax is validated immediately when assigned. Palette names, family names, inherited theme values, and other environment-dependent references are resolved at attach or first typeset, when the required context exists.

Fragments are addressed by stable source selectors or tags and resolved against a layout generation. A `FragmentHandle` stores the node, selector, and generation; using it after incompatible relayout fails clearly rather than silently addressing different leaves. `FragmentTag` is an immutable value identifier, not an arbitrary `object`; scene code maps tags to application data separately.

### 12.5 Math and pictures

Fast math uses the in-process adapter. Portable math fallback enters a layout as a backend-neutral `VectorPicture` — an immutable retained list of Tier-1 path/rule/image commands — rather than an opaque `SKPicture`. A backend may cache a native picture privately. Full external-TeX math is an optional load-time decorator and remains vector-first.

### 12.6 Text-on-path and dynamic readouts

Text-on-path is a second placement stage over a cached flat layout, so animating path offset does not reshape. Dynamic numeric layouts use a dedicated fixed-capacity path, invariant-culture formatting by default, and digit/cluster caches explicitly separate from whole-run caches.

### 12.7 Vector export

Glyph outlines are the default vector representation. Bitmap-only/color glyphs or unsupported picture commands rasterize the smallest correct unit and are reported; they are never silently omitted or substituted.

## 13. Ambients — the open extension point

**Engine capabilities are closed (§4.2); author extensions are Ambients.** Typed, discoverable-by-type shared state on the scene:

```csharp
Ambients.Set(new ThemeAmbient { Accent = Color.OkLch(0.7, 0.12, 250) });
var theme = GetAmbient<ThemeAmbient>(); // node/behavior; resolved & cached at attach
```

- Plain author-defined classes; no engine interface. Registered by type in `AmbientRegistry`.
- **Resolution is cached at attach** (§6.6) — steady-state reads are field reads, no per-frame dictionary lookups (§3.6). Interior mutation of the ambient object is the live-update path; replacing a registered instance rebinds only on later attaches (documented).
- **Fallback chain across embedding:** `AmbientRegistry` supports a parent registry; `Get` walks the chain, `Set` writes locally (a child registration **shadows** the parent's). `SceneNode` links the child scene's registry to its own scene's **before the child's `OnLoad`** (`InheritAmbients = true` by default; opt out for isolation). Themes and settings therefore flow into embedded scenes by default — a deck's theme reaches every slide — while any slide may shadow it. The same shadowing design is the reserved seam for future **subtree-scoped** ambients (region-local theme) if a real need appears; the initial implementation keeps scene-level + inheritance only.
- Canonical uses: theme/palette, quality/debug toggles, simulation snapshots (§4.6), shared random `Seed`, presentation state, and — where a scene genuinely needs cross-cutting fan-out — an author-owned event-hub class (§10.5, `Najm.Guard`).

---

## 14. Scene composition (`SceneNode`) and presentations

### 14.1 Model

A `SceneNode<TScene>` owns a complete child scene instance with its own layers, compositor, scheduler, and ambients. It presents the child as an image in the parent while keeping the child's public API available through `Content`.

### 14.2 Driving and communication

The node is the child's sole driver. It forwards a retimed or gated `TickContext`, supplies a render target at the effective render scale, wraps environment capabilities, links ambient fallback, and maps input into child virtual coordinates when interaction is enabled. The child communicates upward through ordinary public signals, properties, methods, and ambients; no special embedding channel exists.

The child's tick nests at the `SceneNode`'s update position. Rendering is safe because child render is idempotent and snapshots remain local to the render call.

### 14.3 Explicit render caching

Najm does not infer scene damage. `SceneNode` instead exposes an author-controlled policy:

```csharp
public enum RenderPolicy
{
 Always, // render the child whenever the parent renders
 WhenInvalidated, // reuse the cached image until InvalidateRender()
 Once // render on first use; explicit invalidation is an error
}
```

`SceneNode` exposes the policy and explicit invalidation directly:

```csharp
public RenderPolicy RenderPolicy { get; set; } = RenderPolicy.Always;
public void InvalidateRender();
```

`Always` is the default. `WhenInvalidated` is appropriate for static diagrams, parked slides, and children whose expensive content changes only at known moments. `InvalidateRender()` marks the cached image dirty; changing render scale, target spec, child virtual resolution, or environment/backend identity invalidates automatically. Tick gating is independent: a cached child may continue ticking, or the parent may pause it explicitly. The policy caches pixels, not simulation state, and never changes child semantics.

The cached image is owned by `SceneNode`, not a call-scoped snapshot. The backend realizes it through a persistent target/image copy or equivalent resource whose lifetime is explicit. Cached rendering reports target bytes and invalidation counts in diagnostics.

### 14.4 Rules and future seams

An embedded scene does not know it is embedded, has no parent pointer, and cannot be driven elsewhere. Scene classes are reusable; instances are not shared. A child image may later be mapped onto a 3D quad. Multi-view remains enabled by render-idempotence and camera resolution at render time.

Deep deck nesting will require measured policies for focus paths, parking/eviction, target budgets, and replay tests. Those are roadmap work rather than implicit behavior.

## 15. Development workflow

Priority order: fast iteration, debuggability, then packaging.

- `dotnet watch` applies ordinary method-body edits in place whenever the runtime supports them.
- A host-reserved manual warm-restart command, default `F5`, reconstructs the scene from its factory while retaining the environment, loaded native libraries, asset caches, and surface pools. This is the reliable baseline for changed coroutine suspension structure, type shape, constructors, and other rude edits.
- Automatic rude-edit classification is optional later work. It must be proven in an isolated runtime spike before becoming a milestone dependency; the architecture does not assume that `updatedTypes` always identifies active iterator state machines precisely.
- `Run(sceneInstance)` remains single-run sugar and cannot warm-restart because no factory exists.

The debug overlay reports frame timing, node/batch counts, scheduler ownership, resolved bounds, composition brackets and peak depth, backdrop barriers, target and pool estimates, text/content transitions, capture queue drops, managed allocations/GC collections, provider surfaces, Skia cache usage, and current render scale.

DEBUG hooks include `ForceBracket`, `ForceCanonicalPath`, bounds visualization, and phase/lifecycle assertions. Crashes go to the ordinary debugger; Najm does not build an in-engine crash UI.

## 16. Solution layout

**Dependency rule:** everything points inward to `Core`; backends implement Core abstractions; backends never depend on each other; `Lib` depends only on Core/Utils; hosts and `App` compose. **Utils vs Core tiebreak: when in doubt, Core** — do not burn time on the taxonomy.

| Project | Depends on | Responsibility |
|---|---|---|
| **Najm.Utils** | System.Numerics | Pure utilities: math, easing/timing (`Ease`, `ITimingFunction` families, `CubicBezier`), curves (cubic Bézier, Catmull-Rom), `LookupTable`, `Color` (sRGB + HSL/OKLCH + linear helpers), `Angle`. |
| **Najm.Core** | Utils | The engine model + all abstractions, **no SkiaSharp/Silk.NET**: Node/Behavior + **`Transform2D`/`Transform3D` (cached `InverseWorld`, `TransformTo`/`ToLocalOf`, scale pinning §6.3)** + registry + **unified deferred flush (§6.4)**; the **frame anatomy (§4.7)** and the **Layout pass (§6.8)**; the **composition model (§6.7)**: property set (incl. `Isolate`), isolation bracketing, `EffectGraph` descriptors **with their bounds transforms**, mask slots, `ZIndex` ordering; Scene/Layer; **`ICompositor` (the composited-path contract) + the render traverser + the direct path**, incl. `ReadsBackdrop` staging semantics, layer `Blend`/`Effect`, color policy §3.4, and the degradation pipelines; `IDrawContext2D` + `DrawContext2DBase` (conveniences **and bulk-tier portable defaults**, §7.2–7.3, portable bracket realization §6.7, **the backend-facing composition SPI**) + descriptors/handles (`Paint`, `FillRule`, blend subset, `PushTransform`, `VectorPolicy`, `IImage.CopyPixels` + `PixelFormat`); **the Core text model: `ITypesetter`, `TypesetRequest`, `ITextLayout` + the run vocabulary (`GlyphRun`/`RuleRun`/`VectorPictureRun`), `FontFace`, `FontFamily`, `Style`, `RichContent`, the markup grammar, the fragment model, `PathSpec`/`PathPlacement`**; **fail-loud null capabilities: `NullTypesetter`, `NullAudioSink`**; 3D model + `IDrawContext3D` (**compose semantics** §8.2) + `ProjectionDrawContext3D`; input model + **the hit walk (§9.2)** + router + `IInteractive`/`PointerArgs`; `TimeInfo`/`ClockPolicy` (frame–time convention §2.3) + the **coroutine scheduler, tween pass, and `Signal` to the full §10 contract**; Ambients (+fallback chain); `SceneEnvironment` capability interfaces; **`IFrameSink`**; **`IVectorTargetWriter`**; **`OfflineRenderer` and `VectorExporter`** (pure loops over an injected environment). |
| **Najm.Skia** | Core, SkiaSharp | `SkiaDrawContext2D` (+ power tier §7.5; bulk-tier fast paths via `DrawPoints`/`DrawAtlas`/`DrawVertices`); **`SkiaCompositor`**; **both `ISurfaceProvider`s — the GPU (`GRContext`-bound) provider and the CPU-raster provider over a shared base carrying the pool, accounting, and `CreateCompositor()`**, incl. **`WrapBackbuffer`**; **composition realization: `saveLayer` brackets, mask surfaces, the backdrop constructs (NAJM-COMPOSITOR II §4), filter-graph lowering to `SKImageFilter`**; **the text-run lowering (blob building, the glyph-path cache, the mini-blob readout cache — NAJM-SKIA II.3)**; **GL-texture interop wrap (§7.5)**; `IImage.CopyPixels` realization; image/SVG decoding; **SVG + PDF export contexts and the `IVectorTargetWriter` writers (§7.6)**; **the `SkiaOffline`/`SkiaExport` conveniences**; **the de-facto media backend: `FrameSink.PngSequence` (Skia encoder) and `FrameSink.FfmpegPipe` (raw pixels → spawned ffmpeg)**. Realization detail: NAJM-SKIA (§1.5). |
| **Najm.Text** | Core, SkiaSharp, HarfBuzzSharp, CSharpMath | **The sole `ITypesetter` producer (`Najm.Text.Typesetter`)**: HarfBuzz shaping, rich text+math with baseline unification, the fragment model's production side, text-on-path placement, the CSharpMath (Fast) adapter and the `DviSvgmTypesetter` (Full) decorator (§12.5) — pipeline per NAJM-TEXT (§1.5); **embeds Latin Modern Roman + Latin Modern Math as pinned resources (§12.1)**; exchanges SkiaSharp types with `Najm.Skia` only through backend-owned handle side tables (§4.4) — **the two never reference each other**. Separate project so Skia stays lean and TeX stays swappable. |
| **Najm.Audio** | Core, OpenAL | Device `IAudioSink` realization. (`CueRecorder` and the `TeeSink`/`GainSink` decorators are pure command plumbing and live in Core.) |
| **Najm.Host.Desktop** | Core, Skia, Silk.NET | **Platform only:** window + GL-context creation and currency; event pump → `InputBlock`; letterbox both ways + bar clearing; **constructs the GPU provider over its context (provider code lives in `Najm.Skia`)**; present loop with the pinned order; **live `Capture` tee** (§4.6); host-reserved keys; the hot-reload integration (§15: in-place deltas; **warm fresh-restart via factory re-invocation over the retained environment**, the trigger). |
| **Najm.Lib** | Core, Utils | Mechanism library: 2D drawables **incl. bulk-tier nodes (`ScatterNode`, `Quiver2DNode` — with `PickElement`, §7.3) and `PathRibbonNode` (§6.6)**; widget interaction machinery + plain-skinned `Panel`/`Label`/`Button`/`Slider`/`TextBox`; **`DraggableBehavior` + `DragHandleNode`** (§9.3, pinned by default §6.3); **`RoutineBehavior`** (§6.2) and **`YSortBehavior`** (§6.7); **arrangement helpers** (`NextTo`/`AlignTo`/`Distribute`/`Arrange`, §6.8, **incl. camera-aware overloads for pinned targets**); **plot mathematics** (nice-tick algorithms, linear/log scales, data→world mapping — pure functions; the `ctx.Scale` idiom lives here §6.3/§7.4); 3D drawables + batches; `Orbit`/`Fly` camera behaviors; `AnchorBehavior`; **`SceneNode`**; standard Ambients (`ThemeAmbient`). |
| **Najm.App** | all above | Samples + watch profile; **the only place hosts are constructed.** |
| *(future)* **Najm.Host.Web** | Core, Skia(WASM) | Browser host — zero Core changes by construction. |
| *(future)* **Najm.IPC** | Core | `ISimulationSource` (named shared-memory buffers), double-buffered snapshots swapped host-side, parameter push; surfaced as `SimulationAmbient`. Design: NAJM-IPC (§1.5). |
| *(future)* **Najm.GL3D** | Core, Silk.NET | `GpuDrawContext3D : IDrawContext3D` — depth buffer, MSAA, instancing, native splat rasterization, field-based volume rendering evaluating the same `BatchCompose` semantics per fragment (§8.5); private render-to-texture handed to the compositor as a texture-backed `SKImage`. Design: NAJM-GL3D (§1.5). |
| **Najm.Guard** | Core, Utils, Lib (public API only) | The owner's opinionated house library (§16.1). |

### 16.1 Najm.Guard — the house library (mechanism vs. taste)

Heavily opinionated components split out of the engine, following the shadcn model: **components you own beat components you configure** — taste-heavy components resist parameterization; clone-and-restyle ends the theming treadmill immediately.

- **`Najm.Lib` = mechanism** (ships with the engine, API-stable, taste-neutral): everything cloners should never have to reimplement *correctly* — widget interaction machinery, plot math, batches, dragging, arrangement, embedding, anchoring.
- **`Najm.Guard` = taste** (maintained opinionated by the owner; others clone and mutate, forgoing updates): plot visual assembly (axes styling, fonts, spacing, legends), styled widgets, **deck/presentation tooling** (transitions, signal-driven stepping, **signal-log record/re-raise utilities**, replay-based back-navigation), optional **pacing helpers** for the simple unifiable live/offline cases (§2.5), an optional **event-hub Ambient** for scenes that want fan-out (§10.5), and set pieces (e.g., the sorting-array visualizer).
- **The guard rule:** `Najm.Guard` (like `SceneNode`) builds against **public Core/Lib API only** — a continuously running proof of engine flexibility. The day a Guard component needs an internal — including a composition bracket the `EffectGraph` algebra can't express (§6.7) — an engine gap has been found; fix the engine.
- **Name:** **`Najm.Guard`**. It is the owner's opinionated layer and a standing public-API integration test.

---

## 17. Implementation roadmap

Milestone scope and acceptance criteria live in **`ROADMAP.md`**. The roadmap begins with a polished 2D “golden loop” vertical slice rather than implementing every long-term contract at once. This architecture remains the semantic target; roadmap stages identify which portions are active implementation commitments and which remain provisional until exercised.

## 18. Verification

**Build/run:** `dotnet build Najm.sln -c Debug` green per milestone; `dotnet run --project Najm.App`; visual confirmation of each milestone's samples.

**Unit tests (window-free):**
- Transform composition **convention tests, one per matrix type** (§3.2): translated parent + rotated child → known world position, for `Transform2D` (`Matrix3x2`) and `Transform3D` (`Matrix4x4`).
- **`TransformTo`/`ToLocalOf` round-trip:** a point mapped node→node→back is identity within epsilon; agrees with the two-step world composition (§6.3).
- `WorldToVirtual` ↔ `VirtualToWorld` round-trips (2D); `Camera3D` projection + `VirtualPointToRay`.
- Registry attach/detach incl. nesting; **deferred-flush timing per phase (§6.4):** Input-attached node Updates, Layouts, and Renders the same frame; Update-attached node Layouts and Renders the same frame with first Update next frame; Layout-attached node Renders only; detach releases capture, cancels routines **and node-owned tweens (stopping at current value)**, unregisters layout.
- Router state machine: hover/capture/focus/consumption; capture release on detach; viewport coordinate mapping; `PointerArgs` local/virtual agreement under a transformed world node; **hit order = exact reverse paint order under `ZIndex` reordering (§6.5, §9.2)**; **`Clip` gates hits; masks and effects do not (§6.7)**; capture/focus bypass the walk.
- **Coroutine semantics suite (§10, exhaustive):** FIFO drain order incl. same-pass child starts; `Seconds` ≥-with-no-carry under fixed step (0.5 s @ 60 fps = exactly 30 ticks, §2.3); **chained tweens gap-free: two 0.5 s tweens joined by `Wait.For` occupy exactly 60 ticks (tween pass precedes coroutine pass, §10.2)**; signal **frame-latch** (raise before and after the waiter within one frame both release; cleared next frame); `Wait.Any` losers keep running; the full **`Step` table** (Signal bypassed-not-consumed; joined `AnimationHandle` completed; joined coroutine join-released, child not force-run); `Cancel` → **synchronous `Dispose`/`finally`**; fault → `Faulted` + rethrow + waiter release with branchable `Status`; `Pause` freezes `Seconds` accumulation; **`Enabled = false` on the owner (or an ancestor) suspends owned routines and tweens; re-enable resumes in place (§10.2.5)**.
- **Layout contract (§6.8):** anchor→anchor chain resolves in one frame (rank derivation); rank cycle **fails fast at attach** with the named error; write-own violation asserts in DEBUG.
- Ambient resolution + attach-time caching; **fallback chain + shadowing across `SceneNode`**.
- Depth-sort ordering in `ProjectionDrawContext3D`; within-batch sort + coarse-key interleaving.
- **`Max` exactness:** a hard-footprint batch matches a reference per-pixel argmax; `MaxPerChannel` ≡ `Max` on a monochromatic ramp and documented-divergent on polychrome data.
- **Convenience *and bulk* primitive parity** across `SkiaDrawContext2D` (raster) and the SVG export context (§7.6) — the honest second implementation, Tiers 1–2.5 — including true group opacity for arbitrary custom drawables and the verified single-primitive fast path for explicitly atomic built-ins (§6.7).
- **Scale-pinning goldens (§6.3):** a pinned (`ScaleMode.Virtual`) label holds constant virtual size under camera zoom and still scales with `RenderScale`; an `Inherit` stroke zooms; a pinned node under a rotated, non-uniformly scaled ancestor keeps rotation and unit scale (polar decomposition); **zoom ≡ crop:** an all-`Inherit` subtree at zoom 2z matches a 2× upscale of the zoom-z crop within resample tolerance.
- **`FillRule` golden:** an even-odd annulus renders with a hole; non-zero fills it.
- **Composition suite (§6.7):** *isolation no-op equivalence* — a node with default composition properties renders byte-identically inline vs. force-bracketed (via `Scene.Debug.ForceBracket`, §15); *pipeline-order golden* — `Effect = Glow` on a masked unit halos the masked result (effect-after-mask), and the nested inverse (mask outside effect) bounds it; *mask semantics* — `Alpha` vs. `Luminance` channels and `Invert` each match references; *`ZIndex`* — paint order follows (`ZIndex`, insertion) stable sort while Update order stays insertion; *`ReadsBackdrop` idempotence* — tick once, render twice with a `ReadsBackdrop` layer → identical hashes; *invert-lens golden* — `ReadsBackdrop` + `Difference` over known content matches reference.
- **Compositor suite (realized as NAJM-COMPOSITOR §10):** *fast ≡ canonical* — an FP-1-eligible scene renders byte-identically with the fast path enabled vs. `ForceCanonicalPath`; *direct-path blend-scope golden* — a `Multiply` node in an upper layer never samples lower layers on either path (the per-layer bracket); *lerp-merge golden* — the `ReadsBackdrop` merge matches the closed-form lerp, including over transparent below-merge; *`Backdrop` region golden* — frost confined to resolved subtree geometry ∩ active clip, shaped by the clip; *`Backdrop`-independence golden* — fading the unit's `Opacity` does not fade the replaced backdrop (the pin); *viewport 1:1 crispness* — a pixel-grid pattern in a viewport'd layer survives placement exactly; *steady-state pool stability* — N warm frames of a stable topology meet the zero-allocation target and produce zero pool events (the wording made testable).
- **`VectorPolicy` structural check:** a hybrid PDF export contains vector text/axes and exactly one embedded raster image for the `Raster`-flagged subtree.
- **Determinism:** two fresh-instance fixed-step replays of a sample scene → **byte-identical frame hashes** (CPU raster) **per pinned environment** (§2.2); fonts pinned via `IAssets` (§12.1). GPU-backed images compare with tolerance — driver variance is real.
- **Render idempotence:** `Tick` once, `Render` twice → identical hashes, **zero observable state delta** (§4.1) — asserted with and without `ReadsBackdrop` layers in the stack.

**Architecture guards (build-time / CI):**
- `Najm.Utils`/`Najm.Core` reference neither SkiaSharp nor Silk.NET (`IFrameSink` and the sink contracts included); `Najm.Lib` references no backend.
- `SceneNode` and `Najm.Guard` compile against the **public** Core/Lib surface only.
- A `SkiaDrawable` attached under non-Skia caps **fails fast at attach** (test asserts the throw); a `Max` batch without `Scalars` likewise (§8.2).

**Workflow checks:** method-body hot reload updates a running scene; manual warm restart reconstructs from the factory while retaining the environment and caches.

**Interop check (M4):** a 2D label anchored via `Camera3D.WorldToVirtual` in the Layout phase tracks its 3D point across camera animation.

---

## Appendix A — Checklists

### A.1 Determinism discipline (scene author)
1. Never read wall clock (`DateTime.Now`, `Stopwatch`) — use `tick.Time`.
2. Own randomness: explicitly seeded `Random` field; the engine injects no entropy.
3. No order-dependent external I/O; asset loading only in `OnLoad`/`OnAttach`.
4. Rely on contractual iteration order (§6.5) — never on dictionary/hash order.
5. Prefer time-parametric logic (`f(t)`, tweens) where live/fixed equivalence matters; per-frame integration is mode-dependent by nature.
6. **Scenes intended for deterministic runs take no input** (§2.1) — do not poll, do not route; interactive behavior belongs to a live variant of the core (§2.5).
7. **Wrap coroutine cleanup in `try/finally`** — `Cancel` and detach dispose enumerators synchronously (§10.4), so `finally` is the reliable cleanup point.
8. Verify: two fresh-instance replays hash identically (harness provided; per pinned environment, §2.2).

### A.2 Web-compat passive properties (audited at M5, no implementation)
1. Engine runs fully single-threaded (§3.5) — phases are not threads.
2. A complete frame requires no GL and no post chain (pure-Skia composited + direct paths stand alone).
3. Asset I/O confined to `OnLoad`/`OnAttach` → a web host can prefetch before `OnLoad` under the synchronous asset API.
4. Audio is command data → WebAudio sink slots in.
5. Nothing in Core references windows, files-as-ambient-authority, or wall clocks (concrete frame sinks live in `Najm.Skia`, §16).
6. Expectation set: WASM payloads are tens of MB — flagship demo pages, not blog embeds; SkiaSharp/WASM specifics to be re-verified when `Najm.Host.Web` is specced.

## Appendix B — Reference authoring examples (the intended feel)

### B.1 A scene, a custom drawable, three deliveries

```csharp
public sealed class OrbitalScene : Scene
{
 WorldLayer3D _world = null!;
 ScreenLayer _hud = null!;

 protected override void OnLoad()
 {
 Ambients.Set(ThemeAmbient.Dark);
 var theme = Ambients.Get<ThemeAmbient>();

 _world = Layers.Add(new WorldLayer3D()); // bottom
 var cam = new Camera3D { Position = new(0, 1.5f, 8), Fov = Angle.Deg(55) };
 cam.Behaviors.Add(new OrbitCameraBehavior(target: Vector3.Zero, period: 24f));
 _world.Camera = cam; // auto-attaches to layer root

 _world.Root.Add(new PointCloudNode {
 Points = Orbital.Sample(n: 2, l: 1, m: 0, count: 40_000, seed: 7),
 PointSize = 2f, // virtual units — the 3D contract's own rule (§8.2)
 Color = theme.Ion,
 DepthCue = DepthCue.Fade(0.35f),
 });

 _hud = Layers.Add(new ScreenLayer()); // top; local ≡ virtual here (§3.3)
 _hud.Root.Add(new TexNode(@"|\psi_{2,1,0}|^{2}") { Position = new(96, 88), Size = 44 });
 _hud.Root.Add(new Callout { Position = new(96, 168) });

 Start(Intro());
 }

 IEnumerator<Wait> Intro()
 {
 _hud.Root.Opacity = 0; // true group opacity (§6.7)
 yield return Wait.Seconds(0.4);
 yield return Wait.For(Animate(a => _hud.Root.Opacity = a, 0, 1, 1.0, Ease.CubicOut));
 }
}

public sealed class Callout : Drawable // portable custom drawable
{
 ThemeAmbient _theme = null!;
 IPath _flourish = null!;

 public override void OnAttach()
 {
 _theme = GetAmbient<ThemeAmbient>(); // resolve once (§13)
 _flourish = Env.Assets.Bake(new PathBuilder() // static shape: bake once (§3.6, §7.4)
 .MoveTo(0, 26).CubicTo(70, -14, 140, 62, 210, 26));
 }

 public override void Render(IDrawContext2D ctx)
 {
 // Local space; the context arrives with this node's transform applied (§6.6).
 var stroke = Paint.Stroke(_theme.Accent, width: 3, cap: LineCap.Round);
 ctx.DrawCircle(new(0, 0), 6, Paint.Fill(_theme.Accent));
 ctx.DrawLine(new(6, 0), new(210, 0), stroke);
 ctx.DrawPath(_flourish, stroke); // by handle; per-frame dynamic
 } // geometry would use DrawPath(PathBuilder)
}
```

The same scene class, unmodified: `DesktopHost` for live orbiting, `SkiaOffline.Render` for a YouTube clip, `SkiaExport.Pdf(..., at: t)` for the paper figure (the conveniences over `OfflineRenderer`/`VectorExporter`) — the point cloud landing in the PDF as vector geometry, or as an embedded raster under `VectorPolicy.Raster` when it is too dense to be a sane vector file (§7.6).

### B.2 Core + variants (§2.5 in practice)

```csharp
// The core owns the tree and the reusable beats; variants own only the top-level driver.
public abstract class SortStepperCore : Scene
{
 protected BarFieldNode Bars = null!;
 protected readonly int[] Data;
 protected SortStepperCore(int[] data) => Data = data; // ctor parameterization (§4.1)

 protected override void OnLoad()
 {
 var layer = Layers.Add(new WorldLayer2D());
 Bars = layer.Root.Add(new BarFieldNode(Data));
 layer.Camera.FitRect(Bars.GeometryBounds);
 }

 protected IEnumerator<Wait> Swap(int i, int j) { /* highlight, tween, settle */ yield break; }
 protected IEnumerable<(int i, int j)> BubbleSwaps() { /* algorithm as data */ yield break; }
}

public sealed class SortStepperLive : SortStepperCore // presenter-paced
{
 public readonly Signal Next = new(); // raised by a click, a key, or a deck
 public SortStepperLive(int[] data) : base(data) { }
 protected override void OnStart() => Start(Run());
 IEnumerator<Wait> Run()
 {
 foreach (var (i, j) in BubbleSwaps())
 {
 yield return Wait.Signal(Next);
 yield return Wait.For(Swap(i, j));
 }
 }
}

public sealed class SortStepperClip : SortStepperCore // timed, for OfflineRenderer
{
 public SortStepperClip(int[] data) : base(data) { }
 protected override void OnStart() => Start(Run());
 IEnumerator<Wait> Run()
 {
 yield return Wait.Seconds(0.6);
 foreach (var (i, j) in BubbleSwaps())
 {
 yield return Wait.For(Swap(i, j));
 yield return Wait.Seconds(0.35);
 }
 }
}
```

One core, two thin drivers. The live variant is presenter-paced (and steppable via `Step`, loggable/replayable via the signal log, capturable to video via `Capture`); the clip variant renders deterministically with no input existing at all. Nothing in the core knows which world it is running in.

### B.3 Composition in practice (§6.7)

```csharp
// A gradient flowing along a wave, clipped to text set on the same path, the visible
// result haloed by a glow — no targets, no canvas, no brackets; all declarative.
protected override void OnLoad()
{
 var theme = Ambients.Get<ThemeAmbient>();
 var layer = Layers.Add(new ScreenLayer());
 var wave = Env.Assets.Bake(Paths.Wave(from: new(160, 620), to: new(1760, 460), amp: 60));

 var banner = layer.Root.Add(new GroupNode());
 banner.Mask.Add(new TextOnPathNode(@"$\oint \vec E \cdot d\vec A = Q/\varepsilon_0$", wave)
 { Size = 42 }); // markup: $…$ = math (§12.4); placed along the path (§12.6)
 banner.Add(new PathRibbonNode(wave, width: 48) { // arc-length scalar ramp (§6.6, §7.3)
 Ramp = ColorRamp.OkLch(theme.A, theme.B),
 });
 banner.Effect = EffectGraph.Glow(sigma: 10); // Merge(Blur, Source) — one bracket
}
```

Eight lines of scene code: the ribbon supplies the gradient (one `LineBatch2D`, scalars = arc length), the mask slot cuts it to the glyph shapes, and the glow halos the masked result per the pipeline order. Everything is animatable at every joint — tween the ribbon's ramp, slide the mask text along the path — and it exports: mask as SVG `<mask>`/PDF soft mask where expressible, rasterized per `VectorPolicy` where not (§7.6, NAJM-SKIA). The Illustrator idioms follow the same grammar: a shadow overlay is a group with `Blend = Multiply`; a light pass is `Screen`; a vignette is a top node with an inverted radial mask; an invert-lens is a `ReadsBackdrop` layer with a `Difference` node (§5.3).

---
