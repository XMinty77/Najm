using System.Numerics;
using Najm.Utils;

namespace Najm.Core;

/// <summary>Defines the backend-neutral Tier-1 drawing surface used by portable drawables.</summary>
/// <remarks>
/// A render target owns and reuses its context. Callers must not dispose or retain it beyond the
/// target's lifetime. Geometry and paint values are consumed synchronously and are not retained.
/// This interface is pre-release; its contract will be completed before external package
/// publication.
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
    /// engine layer bracket is <em>not</em> an obstacle: the engine owns that bracket, installs the
    /// transform inside it, and closing the bracket unwinds both.
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
    /// M1 scope: the bracket isolates its layer as a group. Node-tier isolation brackets — the ones
    /// a non-default blend or a backdrop read inside the subtree would demand — are not implemented
    /// and are not approximated here.
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
    /// No pass is active, no engine layer bracket is open, or the author state stack is not empty.
    /// </exception>
    void EndLayerBracket();

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
