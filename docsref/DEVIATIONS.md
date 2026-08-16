# Deviations from the reference documentation

The reference set — `ARCHITECTURE.md`, `NAJM-SKIA.md`, `NAJM-TEXT.md`,
`NAJM-COMPOSITOR.md`, `ROADMAP.md` — is the design authority. `PLAN.md` records
execution order and its own "Specification resolutions" for questions the
architecture left open.

This file records the remainder: places where the implementation does something
the reference does not describe, does not specify, or specifies inconsistently.
Every entry states what the docs say, what the code does, and why. An entry is
added at the moment the decision is made, not retroactively.

Statuses: **Open** (decided, not yet implemented), **Implemented**,
**Superseded** (a later doc revision or decision replaced it).

---

## 1. `Scene` has no per-tick hook

**Docs:** §4.1 declares `Scene` with `OnLoad`, `OnStart`, `OnStop`, `OnUnload`
and no per-tick override. `Layer` gets `Update(in TickContext)` (§5.4, "runs
before the layer's tree"); `Node` and `Behavior` get `Update` as well. `Scene`
alone has nowhere to put per-frame logic.

**Decision:** add `protected virtual void Update(in TickContext tick)` to
`Scene`, invoked before the layer traversal.

**Why:** the intended authoring pattern is a `Scene` subclass, and in practice a
generic base scene derived into several concrete scenes. Without a scene-level
tick hook, per-frame logic has to be smuggled into a `Behavior` attached to a
node that exists only to host it. The name is `Update` rather than `OnUpdate`
because the codebase and the docs both use bare `Update` for the per-tick
override on `Node`, `Behavior`, and `Layer`; §3.1's `On`-prefix rule
disambiguates hooks from same-named commands (`Scene.OnStart` vs.
`Scene.Start(routine)`), and no `Scene.Update` command exists to collide with.

**Status:** Open.

---

## 2. `Node.Render` is public, not protected

**Docs:** §6.1 lists `Render(ctx)` among `Node`'s lifecycle members without an
accessibility. §7.5 declares `public abstract override void Render(IDrawContext2D ctx)`
on `Drawable` — the `override` keyword requires a virtual `Render` on the base,
and `public` cannot widen a `protected` base member.

**Decision:** `Node.Render(IDrawContext2D)` is `public virtual`. `Update` stays
`protected virtual`.

**Why:** forced by the docs' own `Drawable` declaration. Noted here because it
makes `Render` and `Update` differ in accessibility, which looks like an
oversight when read cold.

**Status:** Implemented.

---

## 3. `Camera2D`'s world→virtual matrix

**Docs:** §5.2 states only that `Camera2D` is "a `Node2D`: Position/Zoom/Rotation;
`CenterOn`, `FitRect` helpers", that it maps "Y-up world → virtual" and that
"the Y flip lives here". §3.3 fixes virtual space as fixed-resolution, Y-down,
origin top-left. §5.2 also asserts the zoom≡crop invariant: "Camera zoom scales
geometry and every local-unit quantity together." No matrix, no statement of
which world point lands where, no numeric meaning for `Zoom`.

**Decision:**

- `Position` is the world point that lands at the center of virtual space
  (`VirtualResolution / 2`).
- `Zoom` is virtual units per world unit, default `1`. Larger `Zoom` draws
  larger.
- `Rotation` applies about `Position`.
- World `+Y` maps toward virtual `−Y`.

In the row-vector convention used throughout (`PLAN.md` resolution 2):

```
worldToVirtual = Translate(-Position) · Rotate(-Rotation) · Scale(Zoom, -Zoom) · Translate(virtualCenter)
```

**Why:** the zoom≡crop invariant fixes the *direction* of `Zoom` — "scales
geometry" means larger is bigger, which rules out framing-height semantics.
Given that, one world unit to one virtual unit at `Zoom = 1` is the only
fixpoint that does not introduce an arbitrary constant, and center-anchoring is
what makes `CenterOn` and `FitRect` express naturally.

**Status:** Open.

---

## 4. `SkiaExport.Png`

**Docs:** `SkiaExport` is specified with `Pdf` and `Svg` only (NAJM-SKIA V.2).
A single PNG frame has no convenience; it must go through
`SkiaOffline.Render` with a frame sink. There is no stated reason for the
omission.

**Decision:** add `SkiaExport.Png(Func<Scene> make, string path, double at, ...)`,
with the same `at:`-seeking semantics as `Pdf`/`Svg` — run `ceil(at · fps)`
ticks, then render once (§2.3).

**Why:** rendering one frame to inspect it is the most frequent action while
authoring a visual. Routing it through a multi-frame sink means computing a
frame count by hand and discarding all but the last file.

**Status:** Open.

---

## 5. No portable seam for installing render scale and the base transform

**Docs:** NAJM-SKIA II.1 says the traverser's per-node transform is installed via
`canvas.SetMatrix(base × node)`, where base is `RenderScale` × camera mapping —
a Skia call. §5.3 step 2 requires the base transform be installed per layer.
But `IDrawContext2D` exposes no member that sets render scale, caps, or a base
transform, and §7.4 marks `PushTransform` as author-tier, composing "strictly
below" the engine transform. Today the only mechanism is
`SkiaRenderTarget.BeginPass(float, RenderCaps, in Matrix3x2)`, which is
`internal` to `Najm.Skia` and therefore invisible to Core.

As written, Core's traverser cannot drive any backend. The docs do not say
whether pass-begin belongs on `IRenderTarget`, on the backend-facing composition
SPI, or stays backend-internal.

**Decision:** unresolved.

**Why it matters:** every future backend inherits this seam, so it should be
settled deliberately rather than by whatever the first implementation needs.

**Status:** Open — blocking the traverser.

---

## 6. Exception types for "fail loud"

**Docs:** the house rule is stated repeatedly — "author mistakes fail loud;
environment shortfalls degrade correctly and say so" (NAJM-TEXT VI.3), "fail
loudly for unsupported author requests" (`PLAN.md`). Specific failures are
enumerated with required *message content* ("naming the option to set", "with
the character position", "naming the decorator"). But no document maps any
failure class to a .NET exception type, and no `NajmException` hierarchy is
specified.

**Decision:** follow the distribution the existing code already established —
`InvalidOperationException` for lifecycle and contract violations,
`ArgumentOutOfRangeException` / `ArgumentException` for parameter validation,
`InvalidDataException` for malformed asset or font bytes,
`NotSupportedException` for a capability the backend genuinely lacks. No custom
exception base type until something needs to catch Najm failures selectively.

**Why:** consistency with the 165-odd throw sites already in the tree is the
only signal available, and inventing a hierarchy now would be a public-surface
commitment made on no evidence.

**Status:** Open.

---

## Documentation conflicts

Places where the reference set disagrees with itself. Recorded so the
resolution is visible and does not look like an implementation error.

### Fast-path numbering

`NAJM-COMPOSITOR.md` §3 defines FP-1…FP-5 with "FP-5 — direct-path layer bracket
skip". `NAJM-SKIA.md` III.2 calls the same predicate "FP-6's registry-counted
predicate". The compositor document owns the fast-path table, so its numbering
is authoritative. "Registry-counted predicate" appears only in the Skia document
and is never specified; `ARCHITECTURE.md` §5.3 phrases the same rule as "an
enumerable skip condition".

### Easing names

`ARCHITECTURE.md` §10.6 writes `Ease.OutCubic`; Appendix B.1 writes
`Ease.CubicOut`. Already resolved by `PLAN.md` resolution 3 in favour of
direction-first (`Ease.OutCubic`), which is what `Najm.Utils` implements.

### M1 scope: ROADMAP vs. PLAN phasing

`ROADMAP.md` M1 scope includes "Desktop live preview", "Method-body hot reload
plus manual `F5` warm restart", and the debug overlay. `PLAN.md` places the
desktop host, hot reload, and initial diagnostics in **Phase 4**, under
"Provisional scope: phases 4 through 6".

M1 therefore cannot be declared complete by finishing PLAN's approved phases
1–3. Which document governs the M1 boundary is unresolved, and it decides
whether Silk.NET and a windowing loop are in the current body of work or not.

### `Najm.Text`'s CSharpMath dependency

`NAJM-TEXT.md` §0 pins `CSharpMath.SkiaSharp`, and `ARCHITECTURE.md` §16 lists
`Najm.Text`'s dependency row as "Core, SkiaSharp, HarfBuzzSharp, CSharpMath".
`PLAN.md` resolution 7 says the opposite: "Fast math uses `CSharpMath.Rendering`
through a Najm-owned portable canvas that records an atomic `VectorPicture`;
`Najm.Text` does not depend on `CSharpMath.SkiaSharp` or SkiaSharp."

`PLAN.md` is later-dated and claims to resolve gaps "unless later architecture
edits supersede them", and the implemented `Najm.Text.csproj` follows it —
HarfBuzzSharp and Najm.Core only, no SkiaSharp. The architecture test's
allowlist already anticipates `CSharpMath`/`CSharpMath.Rendering`.

Unresolved consequence: `CSharpMath` and `CSharpMath.Rendering` are pinned in
`Directory.Packages.props` and allowlisted by the architecture test, but no
project references either, and no compatibility spike has been run. `PLAN.md`
Phase 1 required that spike, and resolution 7's whole approach depends on
`CSharpMath.Rendering` exposing a canvas seam Najm can implement portably.
That assumption is currently unverified.

### Direct-path bracket predicate

The direct-path layer bracket triggers on "non-default blend or a backdrop"
(§5.3), while the node-tier isolation predicate also includes mask, effect,
opacity below one, and `Isolate` (§6.7). The docs do not say what the direct
path does with a layer whose subtree carries only effects. Unresolved.
