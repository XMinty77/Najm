# Working on Najm

Najm is a C# vector-first 2D procedural graphics and animation engine for educational graphics —
videos, publication figures, and live interactive presentations. `Najm.Core` defines the scene and
rendering contracts and references no backend; `Najm.Skia` realizes them. `README.md` has the
identity and design goals; `docsref/BRIEF.md` is the owner's original statement of intent.

Most of this codebase is written by agents. The conventions below are what keep that legible, so
follow them even where they cost a little effort.

## Verify your work

```bash
dotnet restore --locked-mode Najm.slnx
dotnet build Najm.slnx -c Release --no-restore     # must end at 0 warnings
dotnet test --solution Najm.slnx -c Release --no-build
```

`--solution` is **required**. Without it `dotnet test` prints a one-line complaint and exits
without running anything, which looks like success if you only check for the absence of failures.
The suite is 861 tests and takes about 20 seconds.

Build and test before claiming a change works, and report the counts. `TreatWarningsAsErrors` is
on for `Najm.Utils` and `Najm.Core` only — everything else is held at zero warnings by discipline
rather than by the compiler, so do not introduce one.

There is **no CI**. These commands are the only gate that exists. See `docsref/TECH-DEBT.md`.

## Which document answers which question

The reference set is about 125k tokens and has no table of contents. Do not read it
front-to-back. Go to the document that owns your question.

| Question | File |
| --- | --- |
| Author-observable semantics: lifecycle, timing, coordinate spaces, composition model, bounds, input | `docsref/ARCHITECTURE.md` |
| How the compositor realizes layers, brackets, fast paths, diagnostics | `docsref/NAJM-COMPOSITOR.md` |
| How Skia, GL, export, delivery and the desktop host realize it | `docsref/NAJM-SKIA.md` (Parts I–VI) |
| Text, shaping, layout, math, caches | `docsref/NAJM-TEXT.md` |
| **Why the code differs from the docs** | `docsref/DEVIATIONS.md` |
| Milestone scope and exit criteria | `docsref/ROADMAP.md` |
| What has landed, what is next, and in what order | `PLAN.md` |
| Known shortfalls that are nobody's feature | `docsref/TECH-DEBT.md` |
| What a sample should be, and what previous authors found | `SAMPLES.md`, plus each `samples/*/NOTES.md` |
| Whether a visual idiom belongs in the engine or an author's library | `DESIGN-LANGUAGE.md` |

Cross-references are written `§7.5` (within `ARCHITECTURE.md`) or `NAJM-SKIA I.7` (across
documents) and resolve by grepping for the numbered heading — `grep -n '^### 7\.5'`. Lines in
`ARCHITECTURE.md` and `NAJM-SKIA.md` run up to ~1,800 characters, so a small line range can be a
large read; grep to the heading you want rather than paging.

**Read the XML doc comments at the declaration.** About 38% of `Najm.Core`'s lines are doc
comments across 1,029 public members, they carry the reasoning as well as the contract, and they
are usually more current than the prose.

## The deviation register is the central discipline

**Every place the implementation departs from the reference set gets an entry in
`docsref/DEVIATIONS.md`, written when the decision is made rather than afterwards.** An entry
states what the docs say, what the code does, why, and a status of **Open** (decided, not yet
implemented), **Implemented**, or **Superseded**. There are 37 entries, and the register is the
reason this codebase can be audited by someone who was not there.

Finding the reference set silent, self-contradictory, or wrong is normal and expected — it has
happened 37 times. Add an entry rather than silently picking an answer and moving on.

If the question is genuinely the owner's — a public API shape, a visual tradeoff, who owns a
decision — **record it with its options costed and mark it `Open — question for the owner`, then
implement the documented behaviour and leave the question open.** Entries 36 and 37 are the model:
each lists every shape that would close it and says what each one gives up. Do not invent an API
to close a question that is not yours.

Three entries are Open right now, and two documentation conflicts at the end of the file are
unresolved. Check whether yours is among them before you start.

Numbering: entries are sequential. If you are working alongside other contributors, claim a number
block before writing so two agents do not both add an entry 38.

## Engine contracts that are easy to break

- **Core references no backend.** No SkiaSharp, Silk.NET, HarfBuzzSharp or CSharpMath in
  `Najm.Core`, `Najm.Utils` or `Najm.Lib`. `tests/Najm.Architecture.Tests` enforces this over both
  direct references and the resolved lock graph, so a transitive leak fails too.
- **`Render` is idempotent.** Tick once, render twice: identical pixels, no observable state
  mutation. History is scene state advanced in `Update`, never state accumulated in a render
  target. This is the contract most often broken by code ported in from elsewhere.
- **Determinism.** Fixed-step replays must hash identically in the pinned environment. Traversal
  order is specified; paint order is `(ZIndex, insertion index)` and sorts **siblings only**.
- **Steady-state frames allocate zero managed bytes** on the paths the architecture marks as hard
  targets. Nothing currently fails the build when that regresses — see `docsref/TECH-DEBT.md`.
- **Fail loudly.** Never silently change rendering or vector-export semantics, and never quietly
  ignore an unsupported author request. A loud refusal at the property set beats a silent wrong
  frame.
- **Fast paths require equivalence evidence** against the canonical path — a byte-identical
  comparison, not just a benchmark showing it is quicker.
- **The direct path and the composited path must not drift.** They share one traverser precisely
  so they cannot, and they drifted anyway once inside that type (deviation 11). Direct-path frames
  are asserted byte-identical to composited frames of the same scene; keep it that way.

## This machine

No GPU and no `/dev/dri`, but a real OpenGL ES 3.2 context over llvmpipe is available with no
installs and no environment variables. Counterintuitively it is **faster than Skia's CPU raster**
for ordinary vector content — roughly 2 ms against 18 ms for 1024² with 20 circles. The GPU path
is not a slow fallback here.

- **Headless GL:** `eglGetDisplay(EGL_DEFAULT_DISPLAY)` **fails** with `EGL_NOT_INITIALIZED`. Use
  `eglGetPlatformDisplayEXT` with `EGL_PLATFORM_SURFACELESS_MESA` (`0x31DD`). No X, no `DISPLAY`,
  no Xvfb needed. `src/Najm.Skia/EglNative.cs` and `HeadlessGlContext.cs` already do this.
- **Two silent failure modes.** If the EGL context is not current on the calling thread, Skia GPU
  draws produce **transparent black with no exception**. And after issuing raw GL calls yourself,
  call `GRContext.ResetContext()` before handing back to Skia or you corrupt its state
  assumptions. Neither throws; both look like a drawing bug.
- **Windowed host:** runs under `Xvfb :99 -screen 0 1600x900x24`. Size the screen **larger than
  the window** — GLFW places it at (50,50) with no window manager, and `xwd -id` returns a clipped
  image otherwise. Run `xdotool windowfocus <wid>` before injecting keys or they go nowhere
  (pointer events need no focus). To close, send `WM_DELETE_WINDOW` over Xlib yourself —
  `xdotool windowclose` destroys the window and kills the client with a `BadDrawable`. Capture
  with `xwd -id <wid>`, which reads what the X server holds after `SwapBuffers`, so a window
  presenting black fails the assertion where a GL readback would not.
- **What Xvfb cannot tell you:** no vsync and no compositor, so frame pacing, present latency and
  tearing are untestable; no window manager, so focus policy, resize-by-dragging, fullscreen and
  hi-DPI are untestable. llvmpipe timings predict nothing about real hardware.
- **One real-GPU gap:** a deleted texture under a live `SKImage` merely goes black on llvmpipe and
  will fault on a real driver. Do not treat llvmpipe's tolerance as correctness.
- **Shared box.** It also runs production services — nginx, a mail stack, Docker, local web apps
  on `127.0.0.1:3010-3090`. Environment changes must be additive and must never restart a service
  or upgrade an existing package. Run expensive builds and 4K renders conservatively, and do not
  run parallel native-heavy jobs when memory pressure is elevated.

## Samples are API design tests, and they must leave an artifact

A sample scene has two jobs: be a visual worth showing, and measure what authoring Najm actually
feels like. The second job is the one that has found real defects — circles not existing,
`GroupNode` missing, a subtree being unclippable, fading trails beading at every join in output
that had already passed review. None of those were caught by a test.

So if you are building or extending a sample:

- **Write `samples/<name>/NOTES.md` as you go, not afterwards.** Record findings at the moment you
  hit them. Status sections are updated in place; the findings list only ever grows.
- **Complain loudly and early.** Anything hacky, awkward, surprising, or harder than it should be
  is a finding. Number them, say what you expected, what happened, and what you did instead.
- **A workaround absorbed silently is a failed sample.** If you worked around an engine gap, the
  workaround goes in `NOTES.md` with the gap named.
- Judge severity as an author, and say that is what you are doing. The maintainer re-judges.
- `samples/Najm.Samples.Fractal/NOTES.md` and `samples/Najm.Samples.Pendulum/NOTES.md` are the
  format to copy.

`NOTES.md` is a deliverable, not a courtesy. It is how friction reaches the engine.

## Conventions

- **Commit subjects are a sentence stating intent, not a changelog line.** "Let a clip end where
  the scene ends, not where a sum said it would." "Close the four GPU-offline seams three projects
  kept reporting." "Route a frame's input to the node that is actually on top." No `feat:`/`fix:`
  prefixes, no scope tags, no imperative-mood-plus-filename. Roughly 30–70 characters.
- **Commit bodies are substantial** — around 15 lines, often more for a slice. Say what the
  problem was, what was decided and what was rejected, what it cost, and the resulting test and
  warning counts. The body is where a future reader learns why, so write it for them.
- **Prose style, in commits and docs alike:** plain, specific, and willing to say what is wrong or
  unresolved. Claims carry their evidence. Avoid marketing register and avoid hedging; if
  something is a guess, say it is a guess.
- **Shared files are serialized.** `Najm.slnx`, `Directory.Build.props`,
  `Directory.Packages.props` and `global.json` are changed by one owner at a time. A new
  dependency is the owner's decision, not an implementation detail — propose it, do not add it.
- **Dependencies are centrally pinned and restores are locked.** Regenerate `packages.lock.json`
  only in the project you own. `CSharpMath` and `CSharpMath.Rendering` are already pinned for the
  math work and intentionally unreferenced.
- **`PLAN.md`, `SAMPLES.md` and `DESIGN-LANGUAGE.md` encode priorities and taste** and belong to
  the owner. Propose changes; do not rewrite them.
- **Never commit sample render output.** `samples/**/out/` is gitignored; keep it that way.
