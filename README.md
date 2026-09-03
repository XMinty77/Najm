# Najm

Najm is a C# procedural graphics and animation engine. It's primarily intended for use in education and science communication.

Najm can produce:
- Mathematically literate visuals
- Procedurally scripted animations
- Live presentations
- Interactive demonstrations
- Publication figures (via PDF/SVG export)

Najm sits in the space between offline animation tools such as Manim, web-first frameworks such as Motion Canvas, and general-purpose game engines such as Unity. It is not intended to replace any of them, they do what they do best and they should be part of your workflow if needed. Najm exists for work that needs capabilities from more than one of those worlds at the same time.

> [!IMPORTANT]
> Najm is quite WIP right now, and is under active development. Its public API is not yet stable, clean versioning hasn't been a priority, and several major seams remain on the roadmap. No packages are currently packaged for general use.

## Why Najm?

Najm was initially my fun spare time project for learning computer graphics. I spent a year experimenting with things and eventually I realized I wanted this. I've tried in the past to make educational graphics with existing tools but always found myself lacking something I wanted with each tool. Namely, no tool I used offered all three of these things:

- **Codes like a game engine.** Uses an imperative scene graph, transform hierarchies, attachable behaviors, deterministic update order, coroutines, and clean object-oriented programming. I especially wanted something in C# because it's my favorite OOP language.
- **Draws like a figure.** Builds crisp paths, plots, diagrams, text, equations, annotations, and reusable visual primitives. Unity is amazing for creating content and I tried to force it to work for my use case but it was too weak in this department.
- **Composes like a design tool.** Supports arranging layers and compositing groups with opacity, blend modes, clipping, masking, gradients, images, shaders, effects, all of that good stuff.

Najm is exactly that, an engine that tries to strike a balance between these three things. It exists for visuals that are too dynamic or interactive for a traditional plotting package, too composition-heavy for a game engine to feel natural, and too live or stateful for an offline animation system.

Portability is a really important property that Najm is built around. The same underlying nodes you code for your video can be used to make an interactive live presentation, with slightly different seams under and above them.

## Design goals

- **Vector-first 2D rendering.** Geometry is authored as paths and primitives and remains crisp across render scales. 2D is the current priority, scientific 3D is planned as it is prevalent in a lot of applications.
- **Imperative authoring:** Scenes are programs, not declarative documents or timelines. Drawables issue immediate-mode commands from `Render`, while scene state is updated through the runtime. Najm does not replace a video editor to any capacity and doesn't try to.
- **One portable scene model:** The same scene architecture is designed to support live, variable-step execution and deterministic fixed-step rendering. Interactive and offline productions can share their visual core while using different top-level drivers and engine seams underneath.
- **Determinism as a foundation:** Fixed-step timing, specified traversal order, controlled lifecycle, and reproducible scheduling support offline video, replay, golden-image testing, and scientific repeatability.
- **Backend-neutral semantics:** `Najm.Core` defines the scene and rendering contracts without depending on Skia; backends realize those contracts without leaking native objects into portable scene code. Najm currently is a Skia-first engine but the dependency is not built into the seams, it's reflected in the codebase instead.
- **Composition and typography:** Blending, clipping, group opacity, color-aware gradients, shaped text, mathematical typography, and eventual vector export are part of the engine's identity rather than afterthoughts.
- **Extensibility.** IPC library for external simulations and GPU workflows are planned, as an example.

## Current status

The repository already includes:

- a portable 2D scene graph with nodes, transforms, layers, cameras, behaviors, bounds, and stable `ZIndex` ordering;
- engine-controlled scene lifecycle and deferred tree mutation;
- fixed-step and live clock policies;
- coroutines, waits, and tweens driven by simulation time;
- paths, common shape primitives, fills, strokes, dashes, gradients, images, clipping, blend modes, and subtree opacity;
- CPU and headless-GPU Skia renderers;
- deterministic PNG and FFmpeg video delivery;
- HarfBuzz-based text shaping with pinned Latin Modern fonts;
- external OpenGL texture interop for shader-backed scenes; and
- raster golden tests, lifecycle and determinism tests, architecture-boundary tests, and allocation benchmarks.

Work still ahead includes the desktop live host, complete input routing and presentation support, masks and effect graphs, SVG/PDF export, full mathematics support, richer text, native scientific 3D, and the public packaging story. See the [roadmap](docsref/ROADMAP.md) and [construction plan](PLAN.md) for the current sequence.

> [!IMPORTANT]
> Most of the code is currently being written by agentic coding tools. You can disagree with me and find that distasteful, and I think that's okay. I'm not necessarily pro-AI. I made the decision to rely on AI for writing this project after it took me a couple months to construct a working prototype with a smaller scope. As a micro-electronics student about to graduate, I simply don't have the temporal and mental capacity to pursue this for several more years until it becomes mature enough to be useful.

## Samples

The sample scenes are both showcases and API design tests: if a polished visual is awkward to author, the friction is treated as an engine finding rather than hidden inside the sample.

- **Orrery**: A seamless layered solar-system loop built from Najm's vector primitives, gradients, transform hierarchy, blending, and camera motion.
- **Double pendulum**: Fixed-step RK4 simulation of nearly identical pendulums diverging into chaos, accompanied by fading phase-space trails.
- **Mandelbrot flight**: A GLSL ES fractal rendered into an OpenGL texture and composited with Najm-authored overlays through the GPU Skia backend.

More detail, including the scenes planned next, is in [SAMPLES.md](SAMPLES.md).

## Getting started

### Requirements

- Linux (the currently pinned native Skia and HarfBuzz assets target Linux)
- the exact .NET SDK version in [`global.json`](global.json), currently .NET 10.0.111
- FFmpeg on `PATH` to render sample videos
- EGL/OpenGL support for the GPU and fractal paths. A software renderer such as llvmpipe is sufficient for the headless sample.

Clone the repository, then restore, build, and test the solution:

```bash
dotnet restore --locked-mode Najm.slnx
dotnet build Najm.slnx -c Release --no-restore
dotnet test Najm.slnx -c Release --no-build
```

Render the Orrery sample as PNG stills without requiring FFmpeg:

```bash
dotnet run --project samples/Najm.Samples.Orrery -c Release -- out --stills-only
```

Remove `--stills-only` to render both the stills and `out/orrery.mp4`:

```bash
dotnet run --project samples/Najm.Samples.Orrery -c Release -- out
```

The Pendulum sample accepts the same output-directory and still/video options. To exercise the GPU texture-interoperability path, render selected fractal stills with:

```bash
dotnet run --project samples/Najm.Samples.Fractal -c Release -- \
  still --out out --samples 1
```

## A minimal scene

Najm scenes own layers and a tree of nodes. Custom `Drawable` nodes paint themselves through the portable drawing interface:

```csharp
using System.Numerics;
using Najm.Core;
using Najm.Skia;
using Najm.Utils;

SkiaExport.Png(() => new DotScene(), "dot.png", at: 0d);

sealed class DotScene : Scene
{
    protected override void OnLoad()
    {
        var layer = Layers.Add(new ScreenLayer
        {
            ClearColor = Color.Srgb(0.025f, 0.03f, 0.05f),
        });

        layer.Root.Add(new Dot());
    }
}

sealed class Dot : Drawable
{
    private static readonly Vector2 Center = new(960f, 540f);

    public override Rect GeometryBounds => new(880f, 460f, 160f, 160f);

    public override void Render(IDrawContext2D context)
    {
        context.DrawCircle(
            Center,
            radius: 80f,
            Paint.Fill(Color.OkLch(0.76f, 0.16f, 250d)));
    }
}
```

This example uses screen-space coordinates and exports the loaded state directly to PNG. The sample projects demonstrate more about the engine.

## Repository structure

| Path | Purpose |
| --- | --- |
| `src/Najm.Core` | Backend-neutral scene graph, runtime, timing, scheduling, text contracts, and rendering API |
| `src/Najm.Skia` | CPU/GPU Skia rendering, composition, image interop, and offline delivery |
| `src/Najm.Text` | HarfBuzz shaping, font ownership, layout, and bundled fonts |
| `src/Najm.Lib` | Higher-level authoring nodes and conveniences |
| `src/Najm.Utils` | Color, angles, easing, and curve utilities |
| `src/Najm.Host.Desktop` | Desktop live host: window, GL context, event pump, letterboxing, presentation |
| `samples` | Authored visual productions used to test the real authoring experience |
| `tests` | Contract, golden-image, native-integration, determinism, and allocation tests |
| `benchmarks` | BenchmarkDotNet suites and recorded baselines |
| `docsref` | The architectural reference, subsystem contracts, deviations, and roadmap |

## Documentation

- [Architecture](docsref/ARCHITECTURE.md) - identity, runtime semantics, lifecycle, portability, and author-observable contracts
- [Compositor](docsref/NAJM-COMPOSITOR.md) - target ownership, layer accumulation, isolation, and diagnostics
- [Skia backend](docsref/NAJM-SKIA.md) - raster/GPU realization, delivery, and native integration
- [Text](docsref/NAJM-TEXT.md) - shaping, layout, mathematics, caching, and backend boundaries
- [Roadmap](docsref/ROADMAP.md) - milestone scope and acceptance productions
- [Known deviations](docsref/DEVIATIONS.md) - explicit differences between the reference design and implementation

The reference documents describe both implemented contracts and planned direction. [`PLAN.md`](PLAN.md) is the most direct snapshot of what has landed and what is currently being built.

## Contributing

Najm is early in its development, and I haven't really thought about accommodating contributors. If you'd like to contribute something, please reach out to me via Discord @xminty77.

## License

Najm is available under the [MIT License](LICENSE). Bundled fonts and third-party components retain their respective licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
