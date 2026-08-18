using Najm.Utils;

namespace Najm.Core;

/// <summary>
/// Backend-facing SPI: the presentation one layer contributes to a frame, opened as a group on the
/// direct path by <see cref="IDrawContext2D.BeginLayerBracket(in LayerBracket)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This value is built by the engine's render traverser, not by authors. It carries exactly the
/// layer state the compositor applies when it stages a layer through its own target and merges it:
/// the target's clear, and the opacity, blend, and placement of that merge. A backend that honours
/// all four reproduces composited output on a path that binds no target at all, which is what keeps
/// <see cref="Scene.RenderDirect(IDrawContext2D)"/> — and every direct-path client, the vector
/// exporters among them — from drifting away from <see cref="Scene.Render(IRenderTarget)"/>.
/// </para>
/// <para>
/// Why these four and nothing else. <see cref="Clear"/> rides the bracket because the direct path
/// otherwise has no way to express "this layer's clear is content": filling a rectangle would need
/// either a general rect-fill primitive on <see cref="IDrawContext2D"/>, which the drawing surface
/// does not want, or a <see cref="PathBuilder"/> built per layer per frame, which the frame budget
/// does not allow. <see cref="Opacity"/> and <see cref="Blend"/> are group operations applied when
/// the bracket closes, never per drawn primitive, exactly as a layer's merge applies them.
/// <see cref="Viewport"/> is the region the bracket clips to and the region the clear fills, and it
/// is in <em>device</em> pixels because that is the one space both ends of this call agree on: the
/// engine transform installed inside the bracket varies per node, so a viewport expressed in any
/// local space would have no fixed meaning.
/// </para>
/// <para>
/// What is deliberately absent. There is no transform: installing one is
/// <see cref="IDrawContext2D.SetEngineTransform(in System.Numerics.Matrix3x2)"/>'s job and the
/// bracket must not compete with it. There is no mask, no effect list, and no node-tier isolation
/// flag; those belong to the full <c>BeginUnit</c>/<c>BeginMask</c> composition SPI, which is M2.
/// </para>
/// <para>
/// <c>default(LayerBracket)</c> is the bracket that contributes nothing — a transparent clear at
/// zero opacity over the whole frame. The engine always constructs one explicitly.
/// </para>
/// </remarks>
public readonly record struct LayerBracket
{
    /// <summary>Creates a bracket from one layer's presentation state.</summary>
    /// <param name="clear">The color the bracket's region is filled with when it opens.</param>
    /// <param name="opacity">
    /// The finite group alpha in the inclusive range [0, 1] applied to the bracket's contents when
    /// it closes.
    /// </param>
    /// <param name="blend">
    /// The blend the bracket's contents are composited with when it closes. It is lowered by the
    /// backend, which rejects an undefined mode; validating it here would put reflection metadata on
    /// the frame path for a value <see cref="Layer.Blend"/> has already validated on assignment.
    /// </param>
    /// <param name="viewport">
    /// The device-pixel region the bracket clips to and fills, or null to cover the whole target.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="opacity"/> is not finite and within [0, 1].
    /// </exception>
    public LayerBracket(Color clear, float opacity, BlendMode blend, Rect? viewport)
    {
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity),
                opacity,
                "A layer bracket's opacity must be finite and between zero and one inclusive.");
        }

        Clear = clear;
        Opacity = opacity;
        Blend = blend;
        Viewport = viewport;
    }

    /// <summary>Gets the color the bracket's region is filled with when the bracket opens.</summary>
    /// <remarks>
    /// The fill composes over whatever the bracket's contents start out as, which is nothing, so a
    /// transparent clear is a no-op and an opaque one covers everything the bracket is placed over.
    /// This is the direct path's answer to <see cref="Layer.ClearColor"/>, and it reproduces the
    /// composited reading — a layer's clear is content — for the source-over content M1 allows.
    /// </remarks>
    public Color Clear { get; }

    /// <summary>Gets the finite group alpha in [0, 1] applied when the bracket closes.</summary>
    public float Opacity { get; }

    /// <summary>Gets the blend the bracket's contents are composited with when it closes.</summary>
    public BlendMode Blend { get; }

    /// <summary>
    /// Gets the device-pixel region the bracket clips to and fills, or null to cover the whole
    /// target.
    /// </summary>
    /// <remarks>
    /// Device pixels, on the frame's own integer pixel grid: the compositor stages a viewport'd
    /// layer through a surface whose origin is rounded and whose extent is rounded outward, so a
    /// direct-path clip on the same integer rectangle covers the same pixels the staged surface
    /// would have.
    /// </remarks>
    public Rect? Viewport { get; }
}
