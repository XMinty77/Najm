# Authoring report — double pendulum chaos sample

Status: COMPLETE. MP4 and stills rendered, full solution builds with 0 warnings,
all 451 pre-existing tests pass.

Deliverables:
- `samples/Najm.Samples.Pendulum/out/pendulum.mp4` — 1920×1080, 60 fps, 18s (1080 frames), H.264.
- `samples/Najm.Samples.Pendulum/out/pendulum-00000ms.png`,
  `pendulum-06000ms.png`, `pendulum-12000ms.png`, `pendulum-17000ms.png` — 1920×1080 stills.

## Design summary

- Physics: point-mass double pendulum, standard planar Lagrangian equations of
  motion, θ measured from the downward vertical (which is also virtual space's
  +Y direction — no coordinate flip needed between physics and pixels). Fixed-step
  RK4, 20 substeps per 1/60s tick.
- 5 pendulums, one shared pivot, θ1 offsets of `i × 2.5e-4` rad ("almost
  identical" initial conditions). Verified standalone before writing any engine
  code: RK4 at this substep count shows **zero measurable energy drift** over a
  20s run, and the 2.5e-4–1e-3 rad spread of offsets diverges visibly by ~5-6s
  and is fully chaotic by ~10s — good pacing for an 18s clip.
- Left half: arms + bobs per pendulum. Right half: phase space of the lower
  bob (θ2 wrapped to [-π, π], ω2), one fading Catmull-Rom trail and one glowing
  point per pendulum. Trails and points are two sibling `Node2D` groups
  (`ZIndex 0` / `ZIndex 1`) under one phase-space container — the HARD
  requirement (every trail beneath every point, genuinely by ZIndex) is
  satisfied structurally, not by insertion-order luck.
- Palette: dark navy background, restrained 5-hue cool-to-warm OKLCH arc for
  the pendulum identities, neutral grey arms so five overlapping pendulums
  read as one structure rather than five competing colors.
- No text anywhere — deliberate; text/`Dynamic` layout is explicitly a later
  sample's blocker (SAMPLES.md item 2), and this design needs none.

## What was good

- **The public API is small and it composes.** `Drawable` + `Node2D` +
  `ScreenLayer` + `Scene.Update` + `IDrawContext2D` was the entire surface
  needed. No scheduler, no coroutines, no camera — SAMPLES.md predicted this
  ("arguably not needed") and it was right.
- **`Scene.Update(in TickContext)` is exactly the fixed-step physics hook a
  simulation wants.** `tick.Time.Dt` is the deterministic `1/fps` value, and
  nothing about driving RK4 substeps from it needed any engine cooperation
  beyond that one number.
- **`ZIndex`'s doc comment steers you to the correct design before you can get
  it wrong.** It says outright that it's a *sibling* stable sort. Reading that
  before writing any node made "two sibling groups under one phase-space
  container" the obvious shape, not something discovered by getting the wrong
  answer first (per-pendulum trail+point nodes, trusting draw order — which
  would visually work for one pendulum and silently fail the moment two
  overlap, exactly the trap the sample is designed to expose).
- **`CatmullRomSegments`'s indexer (`spline[i]` → a `CubicSegment`) is exactly
  the seam a fading/tapering trail needs.** The library gives you the whole
  spline as one call (`AddOpenCatmullRom`) for the common case, *and* the
  per-segment cubics for when you need per-segment paint — both from the same
  underlying math, so there's no risk of the two ever drawing a different
  curve. This is a well-designed two-level API and I didn't have to fight it.
- **Tier-2's `DrawCircle`/`DrawLine`/`DrawRect` landed mid-session and were a
  direct, complete replacement for the hand-rolled circle workaround** (see
  below). I regenerated a still after switching and diffed it byte-for-byte
  against the pre-switch render: **identical MD5**. The doc comment promises
  "pixel-identical to [`DrawPath` over `PathBuilderShapeExtensions.AddCircle`]
  by design," and it held exactly, on the first try, with no visual re-check
  needed beyond the hash. That is a rare thing for a convenience API to get
  right and worth calling out as genuinely good engineering.
- **Render idempotence is a real, checkable contract, not just a comment.**
  Structuring `PendulumInstance` so `Advance` (called once per tick from
  `Scene.Update`) does all the physics and caches every render-facing quantity,
  leaving `Render` to only *read*, was the natural shape once I'd read the
  contract — and it made the still-vs-video byte-identity check above
  meaningful rather than accidental.
- **`OfflineOptions`/`SkiaOffline`/`SkiaExport`/`FrameSink.FfmpegPipe` needed
  zero surprises.** Copied the Orrery sample's `Program.cs` shape almost
  verbatim and it worked first try, including piping straight to ffmpeg with
  no intermediate frame files on disk (this box has ~6GB free — that matters).

## What was awkward / missing

- **No Tier-1 circle/arc primitive at the time I started** (see Findings
  below for the full story) — this was the single biggest friction point,
  and it resolved itself mid-session when Tier-2 landed. Between "I started"
  and "it landed," I duplicated Orrery's exact `Shapes.AddCircle` workaround
  (a hand-rolled 4-cubic Kappa-constant approximation) because there was no
  shared home for it. Two samples independently reinventing the same
  17-line cubic-circle helper is itself a small finding: it suggests the
  convenience belonged in the engine from the start, which is exactly what
  happened.
- **`GroupNode` is documented but does not exist.** ARCHITECTURE.md Appendix
  B.3 writes `layer.Root.Add(new GroupNode())` as the idiom for a pure
  grouping/transform node with no visual output of its own. There is no
  `GroupNode` class anywhere under `src/`. Plain `Node2D` fills the role
  perfectly well (this sample uses it for the pivot group and the two
  trail/point sibling groups, and Orrery does the same for its `system`
  node), so this isn't a capability gap — but a reader following Appendix B
  literally gets a compile error, and either the doc should say `Node2D` or
  the engine should ship a `GroupNode` alias. Minor, but a real
  docs-vs-code mismatch.
- **No way to bracket a clip/opacity/transform push around a whole subtree
  from outside it.** `Node.Render` only runs *before* children, with no
  "after children" hook at the node level (only `Layer` gets
  `OnBeforeRender`/`OnAfterRender`). I wanted to clip the entire phase-space
  panel (trails *and* points, both groups) to its rect in one place; instead
  each leaf node (`PhaseTrailNode`) pushes/pops its own clip in its own
  `Render` call. That's correct and cheap here, but it means "clip a
  subtree" is an idiom the author reimplements per-leaf rather than a thing
  you can say once about a parent. (I did *not* need this for
  `PhasePointNode` since a point never overshoots its own coordinate, so I
  only added it where the spline's bulge made it necessary — not a blanket
  workaround, just the one node that needed it.)
- **No per-vertex color/width polyline.** A trail that fades and tapers along
  its length is a completely ordinary visualization need (Orrery's comet
  trails needed the exact same thing, independently, for its orbit trails —
  see its own `OrbitRingNode` comment, which says almost word-for-word what
  I wrote in my own notes before I'd read it). Right now the answer is "draw
  N separate short paths with N separate `Paint` values," which is what both
  samples do. It works, Skia handles the draw-call count fine at these
  scales (5 pendulums × ~100 segments per frame is nothing), but it is
  real duplicated authoring effort for a need that keeps recurring across
  unrelated samples — a `DrawGradientPolyline`-shaped Tier-2/3 convenience
  (per-vertex alpha and/or width) would remove it entirely.

## What surprised me

- **How little of the engine a physics-driven sample actually touches.** No
  camera, no coroutines, no scheduler, no assets, no text. `Scene.Update` +
  five `Drawable`s + `ScreenLayer` was the whole surface. I expected to need
  `WorldLayer2D` for "a physical simulation" and deliberately did *not* use
  it (see the note left in the code/below) — I'm doing my own physics-to-pixel
  mapping by hand for both panels anyway, and physics-Y-down already matches
  virtual-Y-down, so the world/camera Y-flip layer would have added an
  indirection that bought nothing. Worth double-checking with the maintainer
  whether that's the intended reading of when `WorldLayer2D` earns its keep.
- **A live engine gap closing mid-session, and the fix being a genuine drop-in.**
  I didn't expect the byte-for-byte identity to hold on the first try — I
  expected some cosmetic difference from AA/rounding between my hand-rolled
  cubics and the shipped ones. It didn't happen. That's a strong signal the
  Tier-2 work was done carefully (there's a test,
  `ConvenienceCircleMatchesHandRolledCubicsExactly`, that pins exactly this).
- **The chaos itself was easy to get right and hard to get *paced* right.**
  RK4 correctness was a non-issue (verified in minutes, standalone, before
  touching the engine at all). The actual iteration was tuning the initial
  angle and perturbation size so five pendulums stay visually coherent for a
  few seconds, then cascade apart at readable, staggered times across an
  18s clip — that took several render-and-look passes, exactly as the task
  brief asked for, and none of the iteration was fighting the engine.

## Every workaround, and what I wanted instead

1. **Circle drawing**, before Tier-2 landed: hand-rolled 4-cubic Kappa-constant
   `PathBuilder` extension (`Shapes.AddCircle`), copied from Orrery's own
   workaround for the same gap. **What I wanted**: `ctx.DrawCircle(center,
   radius, paint)` directly on `IDrawContext2D` — which is exactly what
   landed, and I switched to it as soon as it appeared, deleting the
   workaround entirely (see `Shapes.cs`, now just the glow-gradient helper,
   which has no engine equivalent).
2. **Fading/tapering phase-space trail**: N separate `DrawPath` calls, one per
   Catmull-Rom cubic segment, each with its own alpha/width `Paint`. **What I
   wanted**: a single polyline/spline draw call taking a per-vertex or
   per-segment color/width ramp, so the trail is one draw call and one
   Skia-level anti-aliased stroke instead of ~100 short overlapping ones per
   pendulum per frame. Not blocking at this scale, but the exact same
   workaround already existed independently in Orrery — see Findings above.
3. **Phase-panel clipping**: pushed/popped a `Rect` clip inside
   `PhaseTrailNode.Render` itself rather than once around the whole
   trails+points subtree, because there is no node-level "after children"
   hook to bracket a subtree from its parent. Not a real problem at this
   scale (one extra push/pop per node, cheap), but worth knowing this idiom
   doesn't compose the way `PushOpacity`/`PushTransform` do at the node
   level.

## Bug I caught by looking, not by reasoning about the code

The first rendered stills showed the phase-space trail occasionally drawing a
long, faint chord clear across the panel. Cause: θ2 is wrapped into [-π, π]
for the x-axis, and when the physical angle crosses the +π/-π seam the
*wrapped* x value jumps by nearly the panel's full width even though the real
motion is continuous — Catmull-Rom happily splined straight across that fake
discontinuity. Fixed by detecting `|Δx| > PhaseWidth/2` between consecutive
samples and treating that as a break: each contiguous run between wraps gets
its own spline, while age/alpha/width still index off each point's true
position in the whole trail, so the fade stays continuous across the break
itself. This is precisely the "render, look, iterate" the task asked for —
it would have shipped as a silent, occasional visual bug otherwise, and nothing
about it would have shown up from reading the code or running tests.

## Verification performed

- Standalone (pre-engine) physics validation: energy drift and divergence
  timing (see Design summary above).
- Visual iteration: rendered stills at multiple times across the whole clip
  after every structural change, looked at each one, caught and fixed the
  wrap-around trail bug and the glow-center bug (see git history — an early
  draft centered every glow brush at the local origin regardless of where
  the geometry it lit actually was, a copy-paste consequence of borrowing
  the zero-center overload of Orrery's `Shapes.Glow`; caught before ever
  rendering because I checked the two call sites against the geometry they
  painted).
- `dotnet build Najm.slnx -c Release`: 0 warnings, 0 errors, whole solution.
- `dotnet test --solution Najm.slnx -c Release`: **451/451 passed**, 0 failed,
  0 skipped.
- Confirmed pixel/byte identity between the pre-Tier-2 and post-Tier-2 still
  renders (`md5sum` match) before treating the switch as done.
- `ffprobe` on the final MP4: h264, 1920×1080, 60/1 fps, 1080 frames, 18.0s —
  matches the requested spec exactly.
