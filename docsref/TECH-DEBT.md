# Technical debt

**Status:** Living register
**Last updated:** 2026-10-06

Known shortfalls that are not features and not deviations from the reference set. A
`DEVIATIONS.md` entry records a place where the implementation and the design documents disagree;
an entry here records a place where the project's own stated standards are maintained by memory
rather than by machinery, or where something is simply missing and nobody's milestone owns it.

Each entry states the shortfall, what it costs, and what closing it would take. Statuses are
**Open** and **Closed**.

---

## D-1. There is no continuous integration

**The shortfall.** No `.github` directory exists. The project has six real invariants — a locked
restore, a zero-warning Release build, the architecture boundary guards, the determinism and
idempotence suites, the raster goldens, and the native Skia/HarfBuzz smoke checks — and all six are
enforced only by someone remembering to run `dotnet build` and `dotnet test` before committing.

**What it costs.** This was tolerable for a single author on a single box, and it stops being
tolerable the moment a second contributor or a second machine is involved. Three specific holes:

- A warning introduced outside `Najm.Utils` and `Najm.Core` does not fail anything, because
  `TreatWarningsAsErrors` is scoped to those two projects (`Directory.Build.props`). "0 warnings"
  is a convention, not a gate.
- `RestoreLockedMode` and `ContinuousIntegrationBuild` are both already conditioned on
  `'$(CI)' == 'true'` in `Directory.Build.props`. The build policy was written expecting CI; the CI
  was never written. Locked restore therefore does not hold on a developer machine unless the flag
  is passed by hand.
- D-2 below — a documented command that silently stopped working — would have been caught the day
  it broke.

**What closing it takes.** One workflow: `dotnet restore --locked-mode`, a Release build whose
output is asserted to contain zero warnings, `dotnet test --solution`, and a second job that
exercises the GPU and host paths headlessly (EGL surfaceless is available on an ordinary Linux
runner with Mesa; the Xvfb host tests need `Xvfb`, `xdotool` and `x11-apps`). Worth also asserting
the warning count rather than trusting `TreatWarningsAsErrors`, so the convention becomes a gate
without changing the scope of warnings-as-errors.

**Status:** Open. Highest-value item in this file, and the cheapest.

---

## D-2. A loosened SDK band silently broke the documented test command

**The shortfall.** `global.json` pins `10.0.111` with `rollForward: latestPatch`, set by commit
`6e898a5`. The box has since moved to SDK 10.0.112, whose Microsoft.Testing.Platform runner
requires the solution to be passed as `--solution`. The command in `README.md` —
`dotnet test Najm.slnx -c Release --no-build` — now prints a one-line complaint and exits
**without running any tests**.

**What it costs.** The failure mode is the bad kind: a reader who checks for failures and sees none
concludes the suite passed. The README has been wrong since the SDK rolled, and nothing noticed
because nothing runs the README.

The general shape matters more than this instance. `rollForward: latestPatch` deliberately accepts
tooling drift in exchange for not having to repin on every patch. That is a reasonable trade, but
it means the toolchain can change under the project between two runs on the same machine, and
without D-1 there is nothing that will tell you.

**What closing it takes.** The README is corrected as of 2026-10-05. The durable fix is D-1 running
the documented commands, so documentation drift and toolchain drift both surface as a red check.
Alternatively, pin `rollForward: disable` and repin deliberately — which trades drift for
maintenance and is the owner's call.

**Status:** Open (README corrected; the class of problem is unaddressed).

---

## D-3. The zero-allocation contract is asserted nowhere

**The shortfall.** `ROADMAP.md`'s M1 exit criteria require representative steady-state core frames
to allocate zero managed bytes after warm-up, and `ARCHITECTURE.md` §3.6 marks specific paths as
hard zero-allocation targets. `benchmarks/` holds three suites — easing, `PathBuilder`, raster Skia
— and a recorded baseline in `benchmarks/M0-BASELINE.md`. None of the 861 tests fails when a warmed
frame starts allocating.

**What it costs.** The contract is stated in three documents and defended by none of them. In a
codebase where most diffs are written by agents, an allocation introduced into a warm path is
invisible: it breaks no test, changes no pixel, and shows up only as frame-time jitter in a live
run on hardware this project does not have.

**What closing it takes.** An allocation-assertion fixture over a representative warmed scene — tick
and render N frames after warm-up, assert zero managed bytes allocated — plus a refresh of the
recorded baseline. Must be a test, not only a benchmark, so it gates rather than reports.

**Status:** Open.

---

## D-4. `Najm.Lib` is one file, so samples keep inventing the same helpers

**The shortfall.** `Najm.Lib` contains `Text/TextNode.cs` and nothing else.
`DESIGN-LANGUAGE.md` exists because sample authors independently reinvented the same visual
idioms — shaped falloff, the scrim, the two-layer glow — and it records three of them still
duplicated across two `Shapes.cs` files.

**What it costs.** `ROADMAP.md`'s own promotion rule says a deferred abstraction becomes scheduled
when "two separate authoring examples independently need the same abstraction". That threshold has
been crossed repeatedly and the promotions have not happened, so each new sample pays the same cost
again and the engine learns nothing from it.

**What closing it takes.** A judgement per idiom, which is why it has not happened mechanically:
`DESIGN-LANGUAGE.md` separates the *correctness* half of each idiom, which belongs in the engine,
from the *taste* half, which belongs in an opinionated library and is the owner's to choose. The
mechanical parts the remaining samples will need — arrowheads, axes, tick generation, plot frames,
numeric readout formatting — are not taste and can be built now.

**Status:** Open.

---

## D-5. `PLAN.md` drifted for seven weeks without anything noticing

**The shortfall.** Between 2026-08-11 and 2026-10-05, `PLAN.md` listed four audit defects as "not
yet fixed" after all four had been fixed, and listed the minimal scheduler and node-tier
composition as remaining after both had shipped. It is the document a newcomer reaches second,
after `README.md`.

**What it costs.** Paid once already: it is the file that would have sent a contributor hunting
defects that did not exist. `DEVIATIONS.md` stayed accurate throughout because its discipline is
per-decision — an entry is written at the moment a decision is made — whereas `PLAN.md`'s accuracy
depends on someone revisiting a summary.

**What closing it takes.** Updated as of 2026-10-05. Structurally: `PLAN.md` should summarize and
point at the registers rather than restate their contents, so there is one place where a fact
lives. The "Progress" section is the part that rots, because it duplicates what `git log` and
`DEVIATIONS.md` already say.

**Status:** Open (content corrected; the structural duplication remains).

---

## D-6. Phase 4's deferral rests on a premise that has been tested and failed

**The shortfall.** `PLAN.md` resolution 8 defers desktop live preview, method-body hot reload, warm
restart and the debug overlay to phase 4, and reasons partly from the claim that development runs
on a headless VPS "where a windowed host cannot run at all". That claim is false, as
`DEVIATIONS.md`'s M1-scope conflict entry now records: the host opens a real X11 window on Xvfb
over llvmpipe, takes synthetic input through the real router, and is asserted against a frame
captured from the X server.

**What it costs.** Nothing yet — warm restart has since landed anyway, and the phasing may still be
right on other grounds. But a resolution that cites a false premise is a resolution nobody can
check, and the two items still outstanding (hot reload, debug overlay) are currently deferred by an
argument that does not hold.

**What closing it takes.** Re-argue the phasing from scope and risk. The honest remaining
constraint is narrower and worth stating in its place: Xvfb cannot exercise vsync, frame pacing,
tearing, focus policy, resize-by-dragging, fullscreen or hi-DPI, and llvmpipe timings predict
nothing about a stage machine.

**Status:** Open — question for the owner.

---

## D-7. No packaging story, and no consumer outside this repository

**The shortfall.** `README.md` states that no packages are published and the public API is not
stable. Nothing is packed, versioned, or consumed by a NuGet reference anywhere; the one external
consumer, `hydrogen-presentation`, pins a git commit as a submodule-style source reference.

**What it costs.** Little today, and it is correctly deferred — a stable public surface is premature
while M1 is incomplete. Recorded because it is the item most likely to be mistaken for an
oversight, and because the pinning arrangement has a real cost already: an external consumer that
pins a commit sees upstream fixes only when someone repins, so findings answered upstream keep
being re-reported against the old floor.

**Status:** Open, deliberately deferred.

---

## D-8. The development box is full, and 3.7 GB of it is build output

**The shortfall.** `/` is at 100% with about 194 MB free on a 96 GB disk. `/home/ubuntu/Najm` is
3.8 GB, of which **3.7 GB is `bin/` and `obj/` across 34 directories** — all gitignored, all
regenerated by a 25-second build.

**What it costs.** A full disk on a box that also runs nginx, a mail stack and Docker is an
operational risk well beyond this project: logs stop writing and services fail in ways that look
unrelated. For Najm specifically it blocks two things already on the plan — a TeX toolchain for the
dvisvgm tier needs on the order of a gigabyte, and 4K render output needs room to land.

**What closing it takes.** `dotnet clean` or deleting the `bin`/`obj` trees reclaims the 3.7 GB
immediately and reversibly. Worth doing periodically rather than once, and worth noting that a CI
runner (D-1) moves most build output off this box permanently.

**Status:** Open — needs the owner's hand, since the box runs production services.
