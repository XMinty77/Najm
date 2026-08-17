# Sample scenes

Real illustrations, authored against Najm's public API the way any author would.
Their purpose is twofold: they are artifacts worth showing, and they are the
project's honest measure of authoring quality.

**Quality bar.** Each must be accurate and must look *genuinely good* — good
enough to stand in a talk or a published video. Not a demo that proves an API
call works.

**Working method.** One scene at a time, one agent each, explored thoroughly. An
agent building one of these is instructed to **complain loudly and early** when
something is hacky, awkward, or harder than it should be. Friction is the
finding. A sample that quietly works around an engine gap has failed at its second
job, so workarounds must be reported rather than absorbed.

**Shared utility requested:** a **Catmull-Rom** spline helper, so graphed curves
read smoothly and antialias natively through Skia rather than as visible polylines.
Most of these need graphing; it should exist before the graphing-heavy ones start.

---

## 1. Double pendulum — chaos

Several double pendulums released from *almost* identical initial conditions,
integrated in `Update`. Left: the pendulums drawn and animated. Right: a phase-space
view, one point per pendulum. Each point trails its recent history, the trail
fading out behind it.

**Hard requirement:** every trail must render beneath every point — genuinely by
`ZIndex`, not by draw-call ordering luck.

Engine demands: stable integration under fixed step; per-pendulum history as scene
state; a fading polyline (alpha varying along the trail); and a real cross-group
paint-order question, since `ZIndex` orders siblings — putting *all* trails under
*all* points means the author must reach for two sibling groups rather than one
node per pendulum. Whether that is obvious is exactly what this sample tests.

Needs: scheduler (arguably not — `Update` suffices), Catmull-Rom for smooth trails,
a good answer for per-segment alpha.

## 2. Spring–mass–gravity system

Masses, springs, and gravity, drawn as considered vector graphics rather than
programmer art. Force vectors annotated over the top. Parameters — simulation time
among them — displayed top-right, above everything.

Engine demands: layered annotation over simulation; arrowheads; **text**, for the
parameter readout and vector labels.

Blocked on: text. A numeric readout is the documented `Dynamic` layout case, so
this doubles as the readout test.

## 3. Orrery — artistic looping background

A solar system loop. Accuracy explicitly does not matter; beauty does. Simple and
beautiful rather than heavily decorated, built from the engine's own primitives.

Engine demands: gradients, transform hierarchies, seamless looping. Would benefit
from a glow effect.

Nearest to buildable today — needs no text and no GL. A good early candidate, and
the most direct test of whether the primitives are pleasant to compose.

## 4. Sine wave construction

A point rotating on a circle, projected onto both axes with dotted guide lines, with
the sine and cosine curves drawn out sideways from those projections as they trace.

Engine demands: dashed strokes (already implemented), synchronized parametric motion,
curve tracing that extends over time. Labels want text but the geometry does not.

## 5. Fourier wrapping

A function wrapped around a circle at a given spatial frequency, with its centre of
mass drawn as a vector. That vector's magnitude is the Fourier magnitude, traced as a
graph opposite while the frequency sweeps.

**The graph must be intelligent:** it remembers which frequency intervals have been
visited and draws only those — so sweeping back and forth fills in rather than
redrawing or lying about unvisited ranges.

Engine demands: the most stateful of the set. Sparse interval-tracking graph state,
smooth curve rendering through Catmull-Rom, and two synchronized coordinate systems.
The clearest test of whether scene-owned state is pleasant to manage.

## 6. Mandelbrot / Julia — GL shader backed

A fractal rendered by a GLSL ES shader on a real GL context, composited through
Najm's seams. Interactively animate the uniforms: translation, rotation, zoom, and
iteration count, the last showing how precision changes the set.

This is the one the author actually asked for originally, and it matters beyond
itself: it is the rehearsal for the hydrogen renderer's integration path. Same seam,
smaller problem.

Blocked on: the GPU surface provider and the external-texture `IImage`
(deviation 9). Verified feasible headless on this machine — EGL surfaceless with
llvmpipe, no hardware needed.

---

## Readiness

| # | Scene | Blocked on |
|---|---|---|
| 3 | Orrery | Catmull-Rom (nice to have); glow (optional) |
| 1 | Double pendulum | Catmull-Rom; per-segment alpha approach |
| 4 | Sine construction | Catmull-Rom; text for labels only |
| 2 | Spring–mass–gravity | **Text** |
| 5 | Fourier wrapping | **Text**; Catmull-Rom |
| 6 | Mandelbrot / Julia | **GPU provider + external-texture IImage** |

Also generally wanted across these: node-tier `Opacity` (fading a group is
currently impossible — ROADMAP puts it in M1 scope and it is absent), and the
minimal scheduler for anything scripted rather than purely time-parametric.
