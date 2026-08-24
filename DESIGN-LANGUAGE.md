# Visual idioms, and which of them should be lifted

Things the sample scenes invented that are **not** in the engine, recorded so they
can be promoted deliberately rather than reinvented a fourth time.

This is a promotion backlog, not a specification. Each entry says what the idiom
is, where it currently lives, why it looks right, and where it belongs — the
engine (`Najm.Core`, portable and unopinionated) or the author's own opinionated
library (`Najm.Guard` or whatever replaces it).

**The pattern this file exists to catch.** Orrery invented a `Shapes.Glow`
helper. Pendulum, authored separately and later, invented the same helper again,
because there was nowhere to get it. The duplication is what exposed it. But only
the *correctness* half was promoted — `Brush.RadialFade` — and the parts below
are the *taste* half, still duplicated across two `Shapes.cs` files.

Provenance: `samples/Najm.Samples.Orrery/Shapes.cs`,
`samples/Najm.Samples.Pendulum/Shapes.cs`, and
`samples/Najm.Samples.Fractal/Nodes.cs` (`InstrumentNode`, which uses the engine
API directly and writes no helper at all).

---

## What already got promoted, and why

`Brush.RadialFade` and `Brush.LinearFade` (commit `3c14272`) exist because both
sample authors hit the same trap: a glow whose ramp ends at `Color.Transparent`
bruises grey through the midtones, since stops interpolate **unpremultiplied**
and `Transparent` is transparent *black*. It survives review on a dark background
because there the two spellings coincide.

The factories make the trap unhittable — RGB never moves, only alpha ramps. That
is a *correctness* concern, which is why it belongs in the engine. Everything
below is *taste*, which is why it does not.

---

## 1. Shaped falloff — "looks like light, not like a gradient"

**Where it lives:** `Shapes.Glow` in both Orrery and Pendulum.

`Brush.RadialFade` is two-stop, so alpha falls off **linearly**. Light does not.
The samples use eight stops on a squared-inverse-square curve:

```csharp
var t = i / (float)(Count - 1);
var falloff = (1f - (t * t)) * (1f - (t * t));
stops[i] = new GradientStop(t, color.WithAlpha(peak * falloff));
```

The curve is the entire difference between a halo that reads as light and one
that reads as a gradient fill. It is cheap — eight stops, `stackalloc`, no
allocation.

**Belongs in:** the opinionated library. The curve is a taste decision, and an
engine that shipped one would be choosing an aesthetic. `Brush.Radial` already
accepts an arbitrary stop span, which is the right engine-level answer.

**Note if lifting:** `Shapes.Falloff` is the same idea inverted — clear at the
centre, opaque at the rim, ramp `t³` — and is how Orrery draws its vignette.
Lift the pair together; they are one idea with two signs.

## 2. The scrim — separation without a panel

**Where it lives:** `InstrumentNode` in the fractal sample.

Before drawing any HUD hairline over busy content, lay down a **large, very soft
disc of the background colour**. In the fractal it is 620 units across for an
instrument about 360 units wide, at alpha 0.80 falling to 0.

Its own comment is the clearest statement of why:

> A soft scrim first, or the hairlines land on whatever the shader happened to
> put there and read as a scratch. **It is a disc rather than a bar so it has no
> edge of its own.**

That is the whole trick. A rectangular panel announces itself as UI; a soft disc
separates foreground from background while remaining invisible. This is the idiom
most worth lifting, because it is the least obvious and it is what makes the
fractal's instrument legible over a churning Mandelbrot.

**Belongs in:** the opinionated library, as something like
`Scrim(center, radius, background, peak)`. It is four lines and pure taste.

## 3. Two-layer glow — a core bloom plus an atmospheric haze

**Where it lives:** Orrery's planet bodies (`Nodes.cs`), and the fractal's
marker and bar head.

One glow reads flat. Two at different radii read physical. Orrery's planets:

| Layer | Radius | Peak alpha |
|---|---|---|
| Wide haze | `r × 5.5` | `0.16 × brightness` |
| Tight bloom | `r × 2.2` | `0.34 × brightness` |

and then a solid disc on top. **The solid core matters as much as the glows** —
a glow with no crisp centre reads as fog rather than as a source.

The composition rule across all three samples is the same three ingredients:

```csharp
// 1. wide soft halo, additive, so it reads as light rather than a sticker
context.DrawCircle(at, radius * 3f,
    Paint.Fill(Shapes.Glow(color, at, radius * 3f, 0.4f), blendMode: BlendMode.Plus));
// 2. (optionally a second, tighter halo)
// 3. small opaque core
context.DrawCircle(at, radius, Paint.Fill(color));
```

`BlendMode.Plus` is not optional to the effect — it is what makes overlapping
glows accumulate the way light does instead of occluding one another.

**Belongs in:** the opinionated library.

**Caveat worth carrying:** `BlendMode.Plus` is raster-only. Per `SAMPLES.md`'s
scope note that is expected rather than a defect, but a glow idiom is therefore
a raster idiom, and any of this appearing in a publication figure destined for
SVG or PDF will need `VectorPolicy.Raster`.

---

## Also duplicated, lower value

`Shade(this Color, float lightnessScale, float chromaScale)` — shifts OKLCH
lightness and chroma while staying in the same hue family, then clamps to gamut.
Present in both Orrery and Pendulum. Genuinely useful for deriving a palette from
one seed colour, and small enough that duplicating it twice more would not hurt,
but it is the kind of thing a palette module should own.
