using System.Numerics;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Defines the backend-neutral drawing surface used by portable drawables.</summary>
/// <remarks>
/// <para>
/// A render target owns and reuses its context. Callers must not dispose or retain it beyond the
/// target's lifetime. Geometry and paint values are consumed synchronously and are not retained.
/// This interface is pre-release; its contract will be completed before external package
/// publication.
/// </para>
/// <para>
/// <strong>Two tiers, one interface.</strong> <see cref="DrawPath"/>, <see cref="DrawImage"/>, and
/// <see cref="Clear"/> are the Tier-1 primitives every backend lowers natively (ARCHITECTURE §7.1),
/// and so is <see cref="DrawText"/>, which alone among them has no portable default at all.
/// The <c>Draw*</c> shape members below them are Tier-2 conveniences (§7.2): they are declared here
/// because a portable drawable only ever sees this interface — <c>ctx.DrawCircle(...)</c> is the
/// documented authoring form — but they are <em>implemented once</em>, in
/// <see cref="DrawContext2DBase"/>, in terms of <see cref="DrawPath"/>.
/// </para>
/// <para>
/// <strong>Implement this interface by deriving from <see cref="DrawContext2DBase"/>.</strong>
/// Implementing it directly means writing the conveniences again, which is precisely the duplication
/// the tier split exists to prevent. The base class re-declares the Tier-1 members abstract and
/// leaves the Tier-2 members virtual, so a backend writes only what is genuinely backend-specific
/// and may still override a convenience where a native lowering is warranted.
/// </para>
/// </remarks>
public interface IDrawContext2D
{
    /// <summary>Gets the normalized specification of the context's current target.</summary>
    SurfaceSpec SurfaceSpec { get; }

    /// <summary>Gets backend capabilities available on the current target.</summary>
    RenderCaps Caps { get; }

    /// <summary>Gets the finite positive physical-pixel scale installed by the render driver.</summary>
    float RenderScale { get; }

    /// <summary>
    /// Gets the current local-to-virtual geometric-mean scale, including pushed transforms.
    /// </summary>
    /// <remarks>
    /// The value is <c>sqrt(abs(det(current linear transform))) / RenderScale</c>. A singular finite
    /// transform has zero scale.
    /// </remarks>
    float Scale { get; }

    /// <summary>
    /// Replaces pixels inside the current clip with a tagged sRGB color, ignoring the transform.
    /// </summary>
    void Clear(Color color);

    /// <summary>Fills or strokes a backend-neutral path.</summary>
    /// <param name="path">The path geometry and fill rule.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    void DrawPath(PathBuilder path, in Paint paint);

    /// <summary>Draws a finished text layout at the current origin.</summary>
    /// <param name="layout">
    /// The immutable layout to draw, produced by the environment's
    /// <see cref="ITypesetter"/>. Its glyph positions are in its own reading frame, so the caller
    /// installs whatever transform maps that frame into local coordinates — for a text node, the
    /// anchor offset and the upright rule's flip (NAJM-TEXT I.9).
    /// </param>
    /// <param name="colorOverride">
    /// A uniform color for every run, or null to use the layout's own
    /// <see cref="ITextLayout.PaintTable"/>. This is how a node's color — including a color tween —
    /// reaches the glyphs without re-typesetting them.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>Tier 1, with no portable default</strong> (ARCHITECTURE §7.1). Every other Tier-2
    /// convenience on this interface is implemented once in <see cref="DrawContext2DBase"/> in terms
    /// of <see cref="DrawPath"/>; this one cannot be, because Core has no glyph rasterizer and
    /// §12.1 forbids building a second one. Each backend lowers it natively.
    /// </para>
    /// <para>
    /// The layout is borrowed and consumed synchronously. A backend may cache a native realization
    /// of it in a side table of its own — the layout is immutable, so such a cache never goes stale
    /// — but it must not store anything on the layout.
    /// </para>
    /// <para>
    /// The overloads I.8 adds for fragment overlays and for on-path placement arrive with the
    /// features that produce them; this is the flat, uniform case they generalize.
    /// </para>
    /// </remarks>
    void DrawText(ITextLayout layout, Color? colorOverride = null);

    /// <summary>Draws an immutable image through an affine mapping.</summary>
    /// <param name="image">The borrowed source image, consumed synchronously.</param>
    /// <param name="imageToLocal">
    /// Maps the image's top-left pixel-edge rectangle <c>[0, Width] × [0, Height]</c> into the
    /// context's current local coordinates.
    /// </param>
    /// <param name="sampling">The portable source sampling mode.</param>
    void DrawImage(
        IImage image,
        in Matrix3x2 imageToLocal,
        ImageSampling sampling = ImageSampling.Linear);

    /// <summary>Fills or strokes a circle. Tier-2 convenience; see <see cref="DrawContext2DBase"/>.</summary>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radius">The finite nonnegative local-unit radius.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <remarks>
    /// Equivalent to <see cref="DrawPath"/> over
    /// <see cref="PathBuilderShapeExtensions.AddCircle"/>, and pixel-identical to it by design.
    /// </remarks>
    void DrawCircle(in Vector2 center, float radius, in Paint paint);

    /// <summary>Fills or strokes an axis-aligned ellipse.</summary>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radii">The finite nonnegative local-unit semi-axes.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <remarks>
    /// Equivalent to <see cref="DrawPath"/> over
    /// <see cref="PathBuilderShapeExtensions.AddEllipse"/>, and pixel-identical to it by design.
    /// </remarks>
    void DrawEllipse(in Vector2 center, in Vector2 radii, in Paint paint);

    /// <summary>Fills or strokes an axis-aligned rectangle.</summary>
    /// <param name="bounds">The rectangle, in local units.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    void DrawRect(in Rect bounds, in Paint paint);

    /// <summary>Fills or strokes a rectangle whose corners are elliptical quarter turns.</summary>
    /// <param name="bounds">The rectangle, in local units.</param>
    /// <param name="cornerRadii">
    /// The finite nonnegative local-unit corner semi-axes, each clamped to half the corresponding
    /// side.
    /// </param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    void DrawRoundRect(in Rect bounds, in Vector2 cornerRadii, in Paint paint);

    /// <summary>Strokes one straight segment.</summary>
    /// <param name="start">The finite local-unit start point.</param>
    /// <param name="end">The finite local-unit end point.</param>
    /// <param name="paint">The stroke descriptor. A fill paint paints nothing, because the
    /// segment encloses no area.</param>
    void DrawLine(in Vector2 start, in Vector2 end, in Paint paint);

    /// <summary>Strokes or fills a run of straight segments.</summary>
    /// <param name="points">The finite local-unit vertices, in order; the span is not retained.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <param name="close">Whether to close the run back to its first point.</param>
    /// <remarks>Fewer than two points describe no contour and paint nothing.</remarks>
    void DrawPolyline(ReadOnlySpan<Vector2> points, in Paint paint, bool close = false);

    /// <summary>
    /// Strokes a run of straight segments whose color and width vary along its length.
    /// </summary>
    /// <param name="points">The finite local-unit vertices, in order; the span is not retained.</param>
    /// <param name="vertexColors">
    /// One color per vertex, or an empty span to take the color or brush of
    /// <paramref name="template"/> for the whole run. The span is not retained.
    /// </param>
    /// <param name="vertexWidths">
    /// One finite nonnegative local-unit stroke width per vertex, or an empty span to take
    /// <see cref="Paint.StrokeWidth"/> from <paramref name="template"/> for the whole run. The span
    /// is not retained.
    /// </param>
    /// <param name="template">
    /// The stroke this run is painted with. Its <see cref="Paint.Cap"/>, <see cref="Paint.Join"/>,
    /// <see cref="Paint.MiterLimit"/>, <see cref="Paint.Dash"/>, <see cref="Paint.BlendMode"/>, and
    /// <see cref="Paint.IsAntialias"/> apply to the whole run; its color and width are the fallback
    /// for whichever ramp is empty. <see cref="Paint.Style"/> is not read — a run is always stroked.
    /// </param>
    /// <param name="close">Whether to close the run back to its first vertex.</param>
    /// <remarks>
    /// See <see cref="DrawGradientSpline"/> for the shared contract: how a per-vertex ramp becomes
    /// per-segment paint, what happens at the joins, and why this is a Tier-2 convenience rather
    /// than the batch primitive it will one day lower to. Fewer than two points describe no run and
    /// paint nothing.
    /// </remarks>
    void DrawGradientPolyline(
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template,
        bool close = false);

    /// <summary>
    /// Strokes a Catmull-Rom spline whose color and width vary along its length.
    /// </summary>
    /// <param name="spline">
    /// The spline, as <see cref="CatmullRom.Open"/> or <see cref="CatmullRom.Closed"/> describes it.
    /// Its own control points index the ramps, and its cubics are drawn exactly as
    /// <see cref="PathBuilderSplineExtensions.AddOpenCatmullRom"/> would have drawn them.
    /// </param>
    /// <param name="vertexColors">
    /// One color per control point, or an empty span to take the color or brush of
    /// <paramref name="template"/> for the whole spline. The span is not retained.
    /// </param>
    /// <param name="vertexWidths">
    /// One finite nonnegative local-unit stroke width per control point, or an empty span to take
    /// <see cref="Paint.StrokeWidth"/> from <paramref name="template"/>. The span is not retained.
    /// </param>
    /// <param name="template">
    /// The stroke this spline is painted with; see <see cref="DrawGradientPolyline"/> for which of
    /// its fields are read.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>What a fading, tapering trail is for.</strong> A polyline or spline whose alpha
    /// falls off toward its tail and whose width tapers with it is the ordinary way to draw a
    /// comet's tail, a phase-space trajectory, or any "where this has been" streak. Before this
    /// existed every author wrote the same loop — one short path and one <see cref="Paint"/> per
    /// segment — and two unrelated samples in this repository wrote it independently, identically.
    /// This is that loop, once, in the engine.
    /// </para>
    /// <para>
    /// <strong>Per vertex in, per segment out.</strong> The ramps are stated at the vertices, which
    /// is where an author knows them — age, arc length, speed — and every segment is painted with
    /// the value at its own midpoint: the mean of its two endpoints, alpha-weighted for color so
    /// that fading toward a transparent tail does not drift in hue. The ramp is therefore sampled
    /// piecewise, not interpolated continuously across a segment; a run of a hundred samples has a
    /// hundred steps and reads as smooth, and a run of four has four and does not. Denser control
    /// points are the answer, not a different call.
    /// </para>
    /// <para>
    /// <strong>The joins, honestly.</strong> Segments are stroked one at a time, so what happens
    /// where two of them meet is a real question and not a detail. Interior segment ends are forced
    /// to <see cref="LineCap.Butt"/> so that neighbours abut instead of overlapping: two translucent
    /// strokes sharing a round cap composite twice over that cap and bead visibly at every join,
    /// which is the artifact this API exists partly to stop authors from shipping. A spline's joins
    /// are tangent-continuous, so its butt ends meet along a shared perpendicular and leave neither
    /// gap nor overlap; a polyline's corner does not, and a sharp one shows a notch on the outside
    /// of the turn no wider than half the stroke.
    /// </para>
    /// <para>
    /// Two residues remain, and both are bounded. First, wherever a join does not fall on a pixel
    /// boundary, two abutting coverage-antialiased edges split one pixel's coverage and composite
    /// separately, dipping a translucent stroke's alpha along a hairline by at most a quarter of its
    /// square — one part in a hundred at alpha 0.2. Second, <see cref="Paint.Cap"/> has to reach the
    /// two ends of an open run, and a <see cref="Paint"/> caps both ends of the path it is on: the
    /// first and last segments therefore carry it at their interior end too, so a non-butt cap
    /// overlaps its neighbour at exactly those two joins and nowhere else. The default,
    /// <see cref="LineCap.Butt"/>, has neither problem and paints the whole run exactly once.
    /// </para>
    /// <para>
    /// <strong>This is semantics, not a fast path.</strong> One managed draw call in, N stroked
    /// paths out — this convenience alone breaks <see cref="DrawContext2DBase"/>'s one-Tier-1-call
    /// rule, and does so deliberately. §7.3's <c>DrawLines(in LineBatch2D)</c>, whose Skia
    /// realization is a single feathered-quad <c>DrawVertices</c> with per-vertex colors, is the
    /// eventual answer and is M2; it will make the joins seamless and the call count one. It will
    /// not change this signature — the API is the semantics, and the batch tier arrives underneath
    /// it as an override. Until then the cost is honest and bounded: a segment per sample per
    /// frame, which Skia handles comfortably at the scales a trail actually reaches.
    /// </para>
    /// </remarks>
    void DrawGradientSpline(
        in CatmullRomSegments spline,
        ReadOnlySpan<Color> vertexColors,
        ReadOnlySpan<float> vertexWidths,
        in Paint template);

    /// <summary>Fills or strokes an elliptical arc.</summary>
    /// <param name="center">The finite local-unit center.</param>
    /// <param name="radii">The finite nonnegative local-unit semi-axes.</param>
    /// <param name="startAngle">Where the arc begins, measured from <c>center + (radii.X, 0)</c>.</param>
    /// <param name="sweepAngle">
    /// How far the arc turns; positive turns from +x toward +y, which is clockwise on screen.
    /// </param>
    /// <param name="mode">How the arc's two ends are joined into a contour.</param>
    /// <param name="paint">The fill or stroke descriptor.</param>
    /// <remarks>
    /// The arc is split so no cubic spans more than a quarter turn; see
    /// <see cref="PathBuilderShapeExtensions.QuarterTurnKappa"/>.
    /// </remarks>
    void DrawArc(
        in Vector2 center,
        in Vector2 radii,
        Angle startAngle,
        Angle sweepAngle,
        ArcMode mode,
        in Paint paint);

    /// <summary>
    /// Backend-facing SPI: replaces the engine transform installed above all author state.
    /// </summary>
    /// <param name="engineToDevice">
    /// The already composed <c>renderScale × layerBase × nodeWorld</c> mapping from the node's local
    /// coordinates to device pixels. It must be finite.
    /// </param>
    /// <remarks>
    /// This member is called by the engine's render traverser, not by authors. Authors use
    /// <see cref="PushTransform"/>, which composes strictly below the engine transform and never
    /// replaces it. The call is a set, not a push: it discards the previously installed engine
    /// transform wholesale and leaves nothing to pop. Author
    /// <see cref="PushTransform"/>/<see cref="PushClip(in Rect)"/>/<see cref="PushOpacity"/> state
    /// composes below the new value, and <see cref="RenderScale"/> is unchanged because the driver
    /// already folded it into <paramref name="engineToDevice"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component of <paramref name="engineToDevice"/> is not finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The author state stack is not empty. Authors must balance their pushes within a single
    /// <c>Render</c> call, so an outstanding push means the engine transform would be installed
    /// under state it does not own. The stack is not changed when this exception is thrown. An open
    /// engine bracket — layer or unit — is <em>not</em> an obstacle: the engine owns those
    /// brackets, installs the transform inside them, and closing one unwinds both.
    /// </exception>
    void SetEngineTransform(in Matrix3x2 engineToDevice);

    /// <summary>
    /// Backend-facing SPI: opens a group that carries one layer's clear, viewport, opacity, and
    /// blend.
    /// </summary>
    /// <param name="bracket">The layer presentation this group applies.</param>
    /// <remarks>
    /// <para>
    /// This member is called by the engine's render traverser, not by authors, and it is the direct
    /// path's equivalent of the compositor staging a layer through its own target and merging it.
    /// Opening clips to <see cref="LayerBracket.Viewport"/> when one is set and fills that region —
    /// the whole target when it is not — with <see cref="LayerBracket.Clear"/>, so a layer whose
    /// subtree draws nothing still contributes its clear. Closing composites everything drawn since
    /// as one group, attenuated by <see cref="LayerBracket.Opacity"/> and combined with
    /// <see cref="LayerBracket.Blend"/>. Both are group operations: they apply to the bracket's
    /// contents once, never to each primitive inside it.
    /// </para>
    /// <para>
    /// Engine bracket depth is tracked separately from author push depth. Author state must be
    /// balanced when a bracket opens and again when it closes, and
    /// <see cref="SetEngineTransform"/> may be called freely inside an open bracket — installing a
    /// per-node transform inside a per-layer group is the whole point. The bracket owns no engine
    /// transform of its own: opening one discards whatever was installed, and the caller installs
    /// what it needs next.
    /// </para>
    /// <para>
    /// Brackets nest last-in-first-out and every one must be closed before the pass ends; an
    /// unbalanced bracket is reported when the pass ends, as an unbalanced author push is.
    /// </para>
    /// <para>
    /// M1 scope: the bracket isolates its layer as a group. Node-tier isolation is a bracket of its
    /// own — <see cref="BeginUnitBracket(in UnitBracket)"/>, which nests inside this one — and the
    /// M2 parts of §6.7 that would also demand isolation, a mask, an effect, or a backdrop read, are
    /// not implemented and are not approximated here.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="LayerBracket.Blend"/> is not a blend mode the backend can lower.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, or the author state stack is not empty.
    /// </exception>
    void BeginLayerBracket(in LayerBracket bracket);

    /// <summary>
    /// Backend-facing SPI: closes the most recently opened engine layer bracket, compositing its
    /// contents with the opacity and blend the bracket was opened with.
    /// </summary>
    /// <remarks>
    /// The engine transform installed inside the bracket does not survive the close; the caller
    /// installs the one it needs next. Author state is unaffected, because it must be empty here.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, no engine layer bracket is open, the innermost open engine bracket is a
    /// unit bracket rather than a layer bracket, or the author state stack is not empty.
    /// </exception>
    void EndLayerBracket();

    /// <summary>
    /// Backend-facing SPI: opens a group that carries one node's subtree opacity and blend.
    /// </summary>
    /// <param name="bracket">The node composition this group applies.</param>
    /// <remarks>
    /// <para>
    /// This member is called by the engine's render traverser, not by authors, and it is the M1
    /// ancestor of the reference's <c>BeginUnit(in UnitParams)</c>. Closing composites everything
    /// drawn since as one unit, attenuated by <see cref="UnitBracket.Opacity"/> and combined with
    /// <see cref="UnitBracket.Blend"/>. Both are group operations applied once to the unit's whole
    /// contents, which is what makes <see cref="Node2D.Opacity"/> true group opacity rather than a
    /// per-primitive alpha multiply.
    /// </para>
    /// <para>
    /// <strong>Bracket sizing is conservative in M1.</strong> The group covers the active clip —
    /// the whole target when nothing narrower is clipped — rather than the node's device-resolved
    /// <see cref="Node2D.VisualBounds"/>. §6.7's snapped visual-bounds rectangle is the M2 shape and
    /// arrives with the resolved-bounds machinery it needs; until then this trades fill rate for
    /// correctness, never the reverse. Every pixel the tight rectangle would have covered is inside
    /// the conservative one, so the composite is identical and only its cost differs.
    /// </para>
    /// <para>
    /// Node bracket depth is tracked with engine layer bracket depth and apart from author push
    /// depth, for the same reason: the traverser installs a per-node engine transform inside every
    /// open unit, so <see cref="SetEngineTransform"/> must tolerate one while still refusing
    /// outstanding author pushes. The bracket owns no engine transform of its own — opening one
    /// discards whatever was installed, and the caller installs what it needs next.
    /// </para>
    /// <para>
    /// Every engine bracket kind — layer, unit, and clip — shares one last-in-first-out order: a
    /// unit opened inside a layer must close before that layer does, and <see cref="EndLayerBracket"/>
    /// refuses to close across an open unit. Every bracket must be closed before the pass ends; an
    /// unbalanced one is reported when the pass ends, naming its own kind.
    /// </para>
    /// <para>
    /// A clip is <em>not</em> here. §6.7's table says clip state alone does not isolate, so a clip
    /// bounds a subtree through <see cref="BeginClipBracket(in ClipBracket)"/> — no offscreen and no
    /// stacking scope — and a node that both clips and isolates opens the clip bracket outside this
    /// one, so the clip bounds what this group captures.
    /// </para>
    /// <para>
    /// M1 scope: opacity and blend. <c>Mask</c>, <c>Effect</c>, and <c>Backdrop</c> are M2; no part
    /// of them is approximated here.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="UnitBracket.Blend"/> is not a blend mode the backend can lower.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, or the author state stack is not empty.
    /// </exception>
    void BeginUnitBracket(in UnitBracket bracket);

    /// <summary>
    /// Backend-facing SPI: closes the most recently opened engine unit bracket, compositing its
    /// contents with the opacity and blend the bracket was opened with.
    /// </summary>
    /// <remarks>
    /// The engine transform installed inside the unit does not survive the close; the caller
    /// installs the one it needs next. Author state is unaffected, because it must be empty here.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, no engine unit bracket is open, the innermost open engine bracket is a
    /// layer bracket rather than a unit bracket, or the author state stack is not empty.
    /// </exception>
    void EndUnitBracket();

    /// <summary>
    /// Backend-facing SPI: opens a group that bounds one node's subtree to a rectangle without
    /// isolating it.
    /// </summary>
    /// <param name="bracket">The clip this group applies.</param>
    /// <remarks>
    /// <para>
    /// This member is called by the engine's render traverser, not by authors, and it realizes
    /// §6.7's <c>Clip</c> for the rectangular case M1 expresses. Opening intersects the current clip
    /// with <see cref="ClipBracket.Clip"/> read under <see cref="ClipBracket.ClipToDevice"/> —
    /// antialiased, and under that mapping rather than in device pixels, so a rotated clip stays
    /// rotated instead of squaring off — and closing releases it.
    /// </para>
    /// <para>
    /// <strong>This bracket must not stage an offscreen.</strong> Bounding is not compositing: a
    /// clip needs one saved clip entry, and §6.7's table says clip state alone does not isolate. A
    /// backend that realized this with a group would both pay for a layer nothing reads and change
    /// the semantics — a descendant carrying a non-default <see cref="Node2D.Blend"/> would then
    /// composite against the clipped subtree instead of against whatever lies beneath the clipping
    /// node, and those two frames differ visibly.
    /// </para>
    /// <para>
    /// It is nevertheless a bracket and not a <see cref="PushClip(in Rect)"/>: it spans the node's
    /// own paint and every descendant's, it is tracked with the engine's own bracket depth rather
    /// than with author state, and <see cref="SetEngineTransform"/> is therefore legal inside it —
    /// which is what lets the clip bound a whole subtree that each leaf reaches through a per-node
    /// transform of its own. A descendant's <see cref="PushClip(in Rect)"/> composes strictly inside
    /// it and cannot widen it.
    /// </para>
    /// <para>
    /// A node that clips and isolates opens this bracket outside its
    /// <see cref="BeginUnitBracket(in UnitBracket)"/>, matching §6.7's semantic order — clip, then
    /// render node and children, then composite with opacity and blend — so that the clip bounds
    /// what the unit's group captures rather than being applied inside it.
    /// </para>
    /// <para>
    /// Clip, unit, and layer brackets share one last-in-first-out order and one imbalance report,
    /// each kind counted and named separately.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, or the author state stack is not empty.
    /// </exception>
    void BeginClipBracket(in ClipBracket bracket);

    /// <summary>
    /// Backend-facing SPI: closes the most recently opened engine clip bracket, releasing its clip.
    /// </summary>
    /// <remarks>
    /// The engine transform installed inside the clip does not survive the close; the caller
    /// installs the one it needs next. Author state is unaffected, because it must be empty here.
    /// Nothing is composited, because nothing was staged.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No pass is active, no engine clip bracket is open, the innermost open engine bracket is of
    /// another kind, or the author state stack is not empty.
    /// </exception>
    void EndClipBracket();

    /// <summary>Saves state and composes a finite local transform below the engine transform.</summary>
    void PushTransform(in Matrix3x2 localTransform);

    /// <summary>Restores the most recently pushed transform.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopTransform();

    /// <summary>Saves state and intersects the current clip with an antialiased rectangle.</summary>
    void PushClip(in Rect bounds);

    /// <summary>
    /// Saves state and intersects the current clip with an antialiased path using its fill rule.
    /// </summary>
    /// <remarks>The mutable path is consumed synchronously and is not retained.</remarks>
    void PushClip(PathBuilder path);

    /// <summary>Restores the most recently pushed rectangle or path clip.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopClip();

    /// <summary>Saves state and begins a true group-opacity layer.</summary>
    /// <param name="opacity">A finite value in the inclusive range [0, 1].</param>
    void PushOpacity(float opacity);

    /// <summary>Restores and composites the most recently pushed opacity layer.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stack is empty or a different state kind was pushed more recently. The stack is not
    /// changed when this exception is thrown.
    /// </exception>
    void PopOpacity();
}
