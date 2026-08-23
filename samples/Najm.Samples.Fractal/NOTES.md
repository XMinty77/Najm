# Najm.Samples.Fractal — author's notes and report on the GL seam

A Mandelbrot flight rendered by a hand-written GLSL ES 3.00 fragment shader into an
author-owned GL texture, wrapped as a Najm `IImage`, and composited through
`IDrawContext2D.DrawImage` like any other image.

This file is written **as I go**. Findings are recorded at the moment I hit them, not
tidied afterwards. Status sections are updated in place; the findings list only grows.

---

## Status

- [x] Read the seam (`GpuSkiaSurfaceProvider`, `GlTextureImage`, `GlTextureOptions`,
      `HeadlessGlContext`, `EglNative`, the `Gpu/` tests, ARCHITECTURE §4.5/§4.6/§7.5,
      NAJM-SKIA I.7).
- [x] Project created, added to `Najm.slnx`.
- [ ] Author-side GL binding (`Gl.cs`).
- [ ] Shader + GL pipeline (`FractalShader.cs`, `FractalGpu.cs`).
- [ ] Camera move (`Flight.cs`) and scene (`FractalScene.cs`, `Nodes.cs`).
- [ ] GPU offline driver (`GpuOffline.cs`) — had to be written by hand, see F-1.
- [ ] Perf measurement on llvmpipe.
- [ ] Palette / route iteration against real stills.
- [ ] Final 1920x1080 60fps MP4 + stills.

---

## Findings

Numbered in the order I hit them. Severity is my own judgement as an author, not the
maintainer's.

### F-1 — There is no GPU offline driver. `SkiaOffline` and `SkiaExport` are raster-only.
**Severity: high for the hydrogen integration.**

`SkiaOffline.Render` and `SkiaExport.Png` both hardcode `new RasterSkiaSurfaceProvider()`
inside their bodies. There is no `SkiaGpuOffline`, no overload taking a provider, and no
option on `OfflineOptions` to select a backend. So the *documented* way to export a scene
(ARCHITECTURE §4.6: `SkiaOffline.Render(() => new PhononScene(), offlineOptions)`) is
structurally incapable of rendering anything that needs `Caps.GpuBacked` — which is
exactly the external-GL-texture case §7.5 introduces.

`OfflineRenderer.Render(scene, surfaces, options)` in Core *does* take an
`ISurfaceProvider`, so the loop itself is fine. What is missing is the ten-line assembly
that `SkiaOffline` is for.

**Workaround:** `GpuOffline.cs` in this sample — build a `HeadlessGlContext`, build a
`GpuSkiaSurfaceProvider.CreateOver(...)` on it, call `OfflineRenderer.Render` directly.
It is short, but every author doing GL interop will write the same file, and getting the
*dispose order* right (provider before GL context; provider owns both if you pass
`ownsGlContext: true`) is exactly the kind of thing a convenience should own.

**What I wanted:** `SkiaOffline.RenderGpu(make, options)` and `SkiaExport.PngGpu(...)`,
or better, `SkiaOffline.Render(make, options, backend: OfflineBackend.Gpu)`.

### F-2 — `PngFileFrameSink` is `internal`. A GPU still cannot be written to a named PNG.
**Severity: medium. Pure friction, but it bites immediately.**

`SkiaExport.Png` is raster-only (F-1), and the sink it uses internally,
`PngFileFrameSink`, is not public. The only public PNG route is
`FrameSink.PngSequence(directory, name)`, which writes `name_000000.png` — a *sequence*
filename for a one-frame stream.

**Workaround:** call `FrameSink.PngSequence` into a scratch directory and `File.Move` the
single frame to the name I actually wanted. Silly, and it means the still-export path in
this sample does file-system gymnastics for no reason.

**What I wanted:** `PngFileFrameSink` public, or `FrameSink.PngFile(path)`.

### F-3 — `SceneEnvironment.Caps` is `RenderCaps.None` on every offline run, GPU or not.
**Severity: high — it makes a documented safety mechanism unimplementable.**

NAJM-SKIA I.7 says, normatively:

> Attach-time check: a drawable holding a wrapped image validates `Caps.GpuBacked`
> (fail-fast on raster/offline configurations …)

That check cannot be written. `OfflineRenderer.Render` and `RenderStill` both do
`scene.Load(new SceneEnvironment(surfaces))` — the `caps` parameter defaults to
`RenderCaps.None`. So on a *GPU* offline run, `scene.Env.Caps` is `None`, and a drawable
that fail-fast validated `Env.Caps.HasFlag(RenderCaps.GpuBacked)` at attach would throw
on the one configuration that is actually correct.

Worse, `ISurfaceProvider` has no `Caps` member at all. `GpuSkiaSurfaceProvider.Caps` and
`RasterSkiaSurfaceProvider.Caps` both exist as public properties on the *concrete* types,
but the portable interface cannot be asked. So even a manual fix inside `OfflineRenderer`
would need a downcast.

The only place caps are honestly available is `IDrawContext2D.Caps`, i.e. *render* time,
one frame too late to be an attach-time contract.

**Workaround:** this sample checks `context.Caps.HasFlag(RenderCaps.GpuBacked)` in
`Render` and throws there, plus a `Debug.Assert`-flavoured guard in the scene's `OnLoad`
that downcasts `Env.Surfaces` to `GpuSkiaSurfaceProvider` (which it must do anyway, F-4).
Both are worse than what the doc promises.

**What I wanted:** `RenderCaps Caps { get; }` on `ISurfaceProvider`, and
`OfflineRenderer` passing `surfaces.Caps` into the `SceneEnvironment` it builds. That is
a two-line change that makes the documented pattern true. (Not made — `src/` is off
limits for me; recording it here as instructed.)

### F-4 — A scene that owns a GL pipeline must downcast `Env.Surfaces`.
**Severity: medium, and probably unavoidable, but it should be *said*.**

To do anything on the GL seam a scene needs `WrapGlTexture`, `ResetGlState` and `Flush`,
all of which live on the concrete `GpuSkiaSurfaceProvider`. `SceneEnvironment.Surfaces`
is typed `ISurfaceProvider`. So every GL-interop scene begins with

```csharp
var gpu = Env.Surfaces as GpuSkiaSurfaceProvider
    ?? throw new InvalidOperationException("…");
```

which is precisely the "service locator" shape §4.5's prose is proud of not having, just
spelled with a cast. I don't think there's a clean alternative short of a
`RenderCaps`-gated typed accessor, and I'd rather have the cast than a registry — but the
docs describe the interop pattern without ever showing this line, and it is line one.

### F-5 — The author-side GL binding does not exist and must be re-written per project.
**Severity: medium, and by design, but expensive at hydrogen scale.**

`Najm.Skia` binds `libGLESv2.so.2` for exactly four `glGetString` calls
(`GlNative`, `internal`). Everything an author's pipeline needs — `glGenTextures`,
`glTexImage2D`, `glGenFramebuffers`, `glCreateShader`, the whole uniform family — the
author brings. The test suite makes the same observation and solves it the same way
(`tests/Najm.Skia.Tests/Gpu/TestGl.cs` is 130 lines of `DllImport`).

That is the architecturally correct call (NAJM-SKIA §16: the GL stack belongs to the
host). But note that the count is now *three* hand-rolled bindings in one repository:
`GlNative`, `TestGl`, and my `Gl.cs`. The hydrogen renderer will be the fourth, and it
will need far more of the API (UBOs, 3D textures, blending, possibly transform feedback).

**What I wanted:** not an engine change — a *documented recommendation*. "Bring
Silk.NET.OpenGL, or copy `TestGl.cs`" in the I.7 prose would have saved me twenty minutes
of deciding whether I was allowed to add a package reference.

### F-6 — The GLSL ES uniform-setting story is where the silence lives.
**Severity: high for hydrogen. Not Najm's fault; recorded because it is the thing most likely to
burn the integration.**

`glGetUniformLocation` returns `-1` for a uniform the linker eliminated, and `glUniform*(-1, ...)`
is a specified **silent no-op**. A shader edit that stops using a uniform therefore turns a live
control into a dead one with no error, no log line, and a frame that still looks plausible.

Every uniform in `FractalGpu` is resolved through `Gl.RequireUniform`, which throws on `-1` and
says which name and why. That is eleven lines and it is the single highest-value thing in this
sample's GL layer. **Do this in the hydrogen renderer from day one**, before the uniform count gets
large enough that nobody notices one going quiet.

### F-7 — The offline loop never calls `Flush`, so the external-texture handoff is correct
### by accident.
**Severity: high for hydrogen. This is the finding I'd most want the maintainer to read.**

`GpuSkiaSurfaceProvider.Flush` documents itself as "a host calls this once per frame after
rendering". `OfflineRenderer.Render` — the only host on this path — never calls it. What
actually forces submission is `Capture`, which does `target.Snapshot()` +
`snapshot.CopyPixels(...)`, and a Ganesh readback flushes implicitly.

That means the ordering guarantee my texture depends on —

> everything Skia recorded that *reads* my texture in frame N is submitted before I
> overwrite that texture for frame N+1

— holds only because the capture path happens to read pixels back every single frame. It
is not stated anywhere as a contract, no code expresses it, and if it ever stopped holding
(a sink that skips frames, a future deferred-capture optimization, a preview host that
presents without reading back) the symptom would be a *one-frame-late fractal*: still
plausible, still smooth, silently wrong. On llvmpipe you would never notice.

**Workaround:** this sample calls `provider.Flush(submit: true)` itself, at the top of
each tick, before touching its texture. It costs nothing here and makes the dependency
explicit rather than incidental. I'd rather not have to.

**What I wanted:** either `OfflineRenderer` flushing the provider per frame through a
capability the interface exposes, or — better — a documented statement in I.7 of exactly
which point in the frame the author is allowed to overwrite their texture. Right now the
rule is stated as "complete your GL work before the wrap is sampled", which answers the
*producer* side and says nothing about the *reuse* side. For a single double-buffer-free
texture, the reuse side is the one that bites.

### F-8 — `GlTextureImage.Dispose()` and `ReleaseGlTexture` are fine; the *ordering* doc is good.
**Not a complaint. Recording it because "it worked fine" is only useful when it is specific.**

The release handshake is the best-designed part of the seam. `TextureReleased` firing on
*flush*, not on dispose, is the correct answer and is documented with the reason. Because
my texture lives for the whole environment, I never needed it — which the XML doc also
says out loud ("A caller that keeps its textures for the environment's lifetime does not
need it at all"). Being told the thing I could ignore was the single most useful sentence
in the file.

The one thing I had to work out for myself: **whose job is deletion.** The answer is in
`GlTextureImage`'s class remarks ("nothing here creates, reallocates, or deletes the GL
texture … The author owns it for its whole life"), and it is unambiguous. It is *not* in
`GpuSkiaSurfaceProvider.WrapGlTexture`'s summary, which is where I looked first; that
member's remarks state rule (3) as "the texture stays alive until the wrap is disposed and
the release handshake has fired", which tells me when I may delete but not that deletion
is mine at all. Minor, but I read the wrong doc first.

### F-9 — Origin: correct, discoverable, and the docs earn their keep.
**Not a complaint.**

`GlTextureOrigin`'s XML doc says the quiet part: "Getting this wrong is not subtle — the
image appears vertically flipped — but it is also not something the wrap can detect, so
the author states it." I read that before writing a line and passed
`Origin = GlTextureOrigin.BottomLeft` first time. It cost me nothing, which is the whole
point of a good default-adjacent enum.

The default being `TopLeft` when the *dominant* interop case is render-to-texture
(bottom-left) is defensible — `default` means "an ordinary uploaded image" — but it does
mean the zero value is the wrong one for the case the type mostly exists to serve. I'd
have taken a compile error over a default here.

### F-10 — `WrapGlTexture` from inside `Render` is genuinely allocation-free, and says so.
**Not a complaint.**

The cache-per-texture-id design means I call `WrapGlTexture` in the node's `Render` every
frame and it is a dictionary hit. That's documented explicitly ("Repeated calls for an
unchanged texture are a dictionary hit and allocate nothing, which is what makes calling
this from inside a render method legitimate"). It removed a design decision I would
otherwise have had to make — whether to cache the `IImage` on the node and invalidate it
myself — and the answer it gave is the one that keeps ownership in the right place.

### F-11 — `ResetGlState` is mandatory, and this environment cannot catch you omitting it.
**Severity: high for hydrogen. The most uncomfortable thing I found.**

`GpuSkiaSurfaceProvider.ResetGlState` documents itself well: "Mandatory after an author's own GL
work and before the next Skia draw … the result of not saying so is corrupt drawing rather than an
error." So I tested what omitting it actually costs here. Two experiments, both at 1920x1080
through the full scene:

1. **Omit `ResetGlState`, but leave my pipeline tidy** (it unbinds its VAO, program and FBO at the
   end of every render). Result: **a byte-plausible, correct-looking frame.**
2. **Omit `ResetGlState` *and* leave every binding dirty** — no `glBindVertexArray(0)`, no
   `glUseProgram(0)`, no `glBindFramebuffer(GL_FRAMEBUFFER, 0)`, which is what a large renderer that
   was not written to be polite actually leaves behind. Result: **still a correct frame.**

So on Mesa/llvmpipe, Ganesh re-establishes enough state per draw that the mandatory call is
*unobservably* mandatory. That is the worst possible property for a rule: an author develops the
integration here, never calls it, ships, and finds out on a real driver — where the failure mode
the docs promise is "corrupt drawing rather than an error".

I kept the call, because the contract says to and because I cannot test the drivers the talk will
run on. But **this VPS cannot be the place the hydrogen integration validates its GL hygiene.**

**What I wanted:** a debug mode on the provider that deliberately dirties GL state after every
author-visible boundary, so a missing `ResetGlState` fails *here* instead of on stage. Something
like `provider.PoisonGlState = true` under `DEBUG`. Cheap to build, and it converts a class of
untestable bug into a loud one.

### F-12 — Reallocating the texture in place works, silently and correctly.
**Not a complaint. Tested, because the brief asked.**

I reallocated the texture — `glTexImage2D` again on the same GL name, alternating 1920x1080 and
1280x720 — on every third frame of a short run, and re-wrapped through
`WrapGlTexture(id, newSize, options)` each frame. Results:

- The GL name never changed (`texture id 1` throughout), which is the realistic resize.
- Najm rebuilt its wrap **in place**. No wrap disposal, no `ReleaseGlTexture`, no cache eviction,
  nothing for me to invalidate. `GlTextureImage.Size` reported the new extent immediately.
- Because `IImage.Size` is honest, deriving the `DrawImage` transform from it instead of hardcoding
  the frame size makes the composite correct at any texture resolution, for free. The shipped node
  does that (`Nodes.cs`), and it is strictly better code than the `Matrix3x2.Identity` it replaced.

The one thing I had to read carefully to get right: **a reallocation at the same id needs nothing,
and `ReleaseGlTexture` is only for an id that will not come back.** That distinction is stated
clearly in `ReleaseGlTexture`'s remarks. Getting it backwards — releasing on every resize — would
have worked too, but would have thrashed the wrap cache every frame.

### F-13 — `OfflineRenderer.RenderStill` re-runs every tick, which for a GPU scene is the whole clip.
**Severity: medium. A contract collision, not a bug.**

A still at time `t` is defined as `ceil(t x fps)` ticks and then one render. For an ordinary scene
whose state is accumulated that is exactly right. For a scene whose tick does a full GPU pass, it
means **a still at 12.9 s runs 774 fractal renders and throws 773 of them away.** I hit this
literally — my first still batch was still going after two minutes and I killed it. Measured:
44.9 s for the 7.2 s still versus 0.65 s for the same picture evaluated directly.

There is no seam to say "this scene is a pure function of time, evaluate it at `t`". Nothing in
`Scene` distinguishes seeking from simulating, and `IFlight.At(t)` — which *is* that function —
is invisible to the engine.

**Workaround:** the sample's `still` mode evaluates the flight itself and hands the scene a
`FixedFlight` of that one frame, then exports at `at: 0`. Same picture, one pass. It is honest here
because this scene genuinely is time-parametric; a scene with accumulated state could not do it.

**What I wanted:** an optional `ISeekable`-flavoured hook the still path uses when a scene offers
one — `bool TrySeek(double seconds)` — falling back to ticking when it does not. The hydrogen
renderer is almost certainly time-parametric too (orbital phase is a function of `t`), so it will
hit exactly this.

### F-14 — A scene whose content is produced in the tick exports empty at `at: 0`.
**Severity: low. Documented, and it still cost me a minute.**

`SkiaExport.Png`'s remarks say it out loud: "At `at: 0` the tick count is zero … a scene that builds
its content in `OnStart` exports empty at zero and populated at any positive time; that is the
contract, not a bug." The same applies to a scene that *renders its texture* in `Update`: at zero
ticks the texture holds whatever `glTexImage2D` left there, which is undefined content.

**Workaround:** `FractalScene.OnLoad` primes the texture with `flight.At(0)` before building the
graph. One extra shader pass at load, and the scene is never in a state where its texture holds
nothing. I think this is the right thing for any author on this seam and it should be in the I.7
prose: **if your texture is produced per tick, produce frame zero at load too.**

---

## Performance

**The machine.** 6 vCPU, no GPU. `llvmpipe (LLVM 20.1.2, 256 bits)`, OpenGL ES 3.2, Mesa 25.2.8,
`GL_MAX_TEXTURE_SIZE` 16384. Brought up by `HeadlessGlContext` through surfaceless EGL with zero
configuration — it worked on the first call and reported itself honestly, which is worth saying
because every other part of getting GL on a headless box is usually a day.

**It is far faster than the brief led me to expect.** At 1920x1080, one sample per pixel, iteration
limits from 120 (surface) to about 2400 (bottom of the descent):

| Configuration | Per frame | Whole 780-frame clip |
|---|---|---|
| 1 sample, full clip | 0.26 s | 3 min 29 s |
| 4 samples (rotated grid), full clip | ~0.7 s | ~9 min |
| One deep still, 1 sample, 2200 iterations | 0.3-0.9 s | — |

llvmpipe vectorizes the fragment shader eight pixels wide, so the Mandelbrot inner loop — which is
pure SIMD-friendly float arithmetic with a shared exit test — is close to the best case for a
software rasterizer. I budgeted for hours and got minutes. Anyone sizing the hydrogen work off "it
will be slow because there is no GPU" should measure before believing it; the shape of the shader
matters far more than the absence of hardware.

**Does the engine's per-frame budget compose with my GL work, or fight it? Neither — nothing is
measuring them together.** (Finding F-17.)

My shader is >95% of every frame. What Najm does — one `DrawImage` of a full-screen texture, a
gradient-filled ellipse, a dozen hairlines — is a few milliseconds. So on this clip they cannot
fight; there is nothing to fight over. But that is a property of my content, not of the seam, and
the seam offers nothing to find out:

- `CompositorStats` exists (`src/Najm.Core/Rendering/CompositorStats.cs`) and counts composition
  constructs, not time.
- `OfflineRenderer` reports frames, not milliseconds.
- There is no hook between "the tick finished" and "the frame was captured", so an author who wants
  to know how their GL time compares with the engine's has to wrap the sink, as this sample does
  for its progress line, and even then measures the two together.

For a fractal that does not matter. For the hydrogen talk it will: a volumetric orbital raymarch
plus real typeset labels plus vector overlays is a case where you genuinely need to know which half
is costing you. **What I wanted:** wall-clock in `CompositorStats`, or an `OfflineOptions.OnFrame`
callback carrying tick time and render time separately.

**One thing that does compose well:** `glFinish()` at the end of my render and Skia's implicit flush
at capture do not double up in any way I could measure. Removing my `provider.Flush(submit: true)`
at the top of the tick changed nothing measurable — the readback path had already submitted
everything. Which is exactly F-7's point: it is free because it is redundant *today*.

## Precision

**Measured by rendering stills and looking, which is the only honest way.** The shader iterates in
`highp float`. The centre crosses the boundary as two floats and is reassembled with the pixel
offset added to the low half first, so the *sample positions* are good to roughly double precision;
that is cheap and worth doing, but it is not the limit. The limit is the iteration itself, where
`z` is O(1) and its absolute error of ~1e-7 is amplified by a derivative that grows with depth.

Probes at the seahorse-valley target, 1920x1080, 2200 iterations:

| Half-height | Magnification | Verdict |
|---|---|---|
| 6.0e-4 | 2 200x | clean |
| 6.0e-5 | 22 000x | clean |
| 1.5e-5 | 87 000x | clean — a mini-Mandelbrot resolves crisply |
| 4.0e-6 | 325 000x | **visibly blocky**; every filament quantized into stair-steps |

So the break is between 87 000x and 325 000x, roughly where the pixel spacing falls to a few times
`float`'s absolute resolution near |c| ~ 0.75. `Flight.ScaleEnd` is 5.0e-5 — **26 000x, a factor of
three inside the last clean measurement and more than six times inside the break.** The clip does
not dissolve into mush at the end because it never goes anywhere near the depth where it would.

I did *not* implement double-single (df64) emulated arithmetic. It would buy another six or seven
orders of magnitude for roughly triple the inner-loop cost, and this clip does not need it. If a
future version does, the place it goes is the two complex multiplies in `shade` and nowhere else.

**What the seam does about any of this: nothing, and it should not.** Precision is entirely inside
my shader. Najm sees an RGBA8 texture. That division is correct and it is the one thing about this
exercise that needed no thought at all.

Two smaller precision notes worth carrying to the hydrogen work:

- The wrap is `GL_RGBA8` and tagged `ColorSpace.Srgb`, so the shader must encode sRGB itself — a
  plain RGBA8 framebuffer does no transfer-function work. `GlTextureOptions` makes the storage and
  the tag independent and says so, which is right, but it does mean an author who computes in linear
  light (as this one does) has to remember to encode on the way out. Getting it wrong gives a
  washed-out frame that looks like a bad exposure, not like a bug.
- `GlTextureOptions.Rgba16f` plus `ColorSpace.LinearSrgb` is available and is the obvious answer for
  the hydrogen renderer, whose output is genuinely HDR (emission accumulated along a ray). I did not
  use it — 8-bit is plenty for escape-time colour after tone mapping, and it halves the texture
  bandwidth on a software rasterizer. But it is there, it is documented, and the pairing is spelled
  out in `SizedFormat`'s remarks.

## What the hydrogen integration should expect

(final section, written last)

### F-15 — `IImage.Size` on a wrap is live, and that is quietly excellent.
**Not a complaint.** See F-12. Because the wrap reports the texture's *current* extent rather than
the extent it was first created at, an author can derive the draw transform from it and never think
about resizing again. Contrast with the alternative design — a wrap that pins its creation size —
which would have made every reallocation a two-sided update.

### F-16 — The instrument proves the composite is real, and cost nothing.
**Not a complaint, and worth saying because it is the part that most needed to Just Work.**

The vignette and the corner instrument are ordinary portable `Drawable`s — `DrawCircle`,
`DrawLine`, `Brush.Radial`, `BlendMode.Plus` — drawn in the same `ScreenLayer` as the wrapped
texture, ordered by `ZIndex`. There is no special handling anywhere: the GL texture is a normal
`IImage`, `DrawImage` is the normal call, and engine-drawn vector content composites over it
correctly, antialiased, with additive glows intact. That is the whole claim of ARCHITECTURE 7.5 and
it is true.

The one thing I could not use is **text**, because the offline environment's typesetter is
`NullTypesetter` and fails loudly by design (correctly — see `SceneEnvironment`'s remarks). So the
instrument reads magnification and iteration count as a rule and a bar rather than as numbers. That
is a fine constraint for this clip and a real one for the hydrogen talk, which will want labelled
axes: **`OfflineRenderer` has no way to inject a real typesetter**, since it builds the
`SceneEnvironment` itself and takes no options. `SkiaOffline.Render` does not expose one either.
Recording it here because the hydrogen figures will hit it on day one.
