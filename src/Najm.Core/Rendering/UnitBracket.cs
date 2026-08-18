namespace Najm.Core;

/// <summary>
/// Backend-facing SPI: the composition a single node's subtree contributes as one unit, opened as a
/// group by <see cref="IDrawContext2D.BeginUnitBracket(in UnitBracket)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This value is built by the engine's render traverser, not by authors. It is the M1 ancestor of
/// the reference's full composition SPI: <c>BeginUnit(in UnitParams { boundsHint, opacity, blend,
/// effect })</c> / <c>EndUnit()</c>. Two of those four parameters exist here — the two §6.7 lists
/// that M1 implements — and the omissions are deliberate rather than defaulted. There is no
/// <c>boundsHint</c> because M1 brackets conservatively (see
/// <see cref="IDrawContext2D.BeginUnitBracket(in UnitBracket)"/>), and no <c>effect</c> because
/// <c>Effect</c>, <c>Mask</c>, <c>Clip</c>, and <c>Backdrop</c> are M2 and must not be approximated.
/// When those arrive this struct grows fields and gains its reference name; the shape is chosen so
/// that growth is additive.
/// </para>
/// <para>
/// Why a unit bracket is not a <see cref="LayerBracket"/> with fewer fields. A layer bracket carries
/// a clear and a device-pixel viewport because a layer is staged through a target of its own that
/// has to be cleared and placed. A node has neither: it composites into whatever surface its layer
/// already owns, over content its own ancestors drew. Folding the two would mean a per-node bracket
/// paying for a viewport test and a clear fill it can never use, on the one path in the engine that
/// runs once per node rather than once per layer.
/// </para>
/// <para>
/// Both members are group operations: they apply once to everything the unit's subtree drew, never
/// to each primitive inside it. That distinction is the whole reason the bracket exists — two
/// overlapping children under <c>Opacity = 0.5</c> must show the half-alpha composite of their
/// union, not two independently halved shapes, and those differ wherever they overlap.
/// </para>
/// <para>
/// <c>default(UnitBracket)</c> is the unit that contributes nothing: zero opacity over
/// <see cref="BlendMode.SrcOver"/>. The engine always constructs one explicitly.
/// </para>
/// </remarks>
public readonly record struct UnitBracket
{
    /// <summary>Creates a bracket from one node's composition state.</summary>
    /// <param name="opacity">
    /// The finite group alpha in the inclusive range [0, 1] applied to the unit's contents when the
    /// bracket closes.
    /// </param>
    /// <param name="blend">
    /// The blend the unit's contents are composited with when the bracket closes. It is lowered by
    /// the backend, which rejects an undefined mode; validating it here would put reflection
    /// metadata on the per-node frame path for a value <see cref="Node2D.Blend"/> has already
    /// validated on assignment.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="opacity"/> is not finite and within [0, 1].
    /// </exception>
    public UnitBracket(float opacity, BlendMode blend)
    {
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity),
                opacity,
                "A unit bracket's opacity must be finite and between zero and one inclusive.");
        }

        Opacity = opacity;
        Blend = blend;
    }

    /// <summary>Gets the finite group alpha in [0, 1] applied when the bracket closes.</summary>
    public float Opacity { get; }

    /// <summary>Gets the blend the unit's contents are composited with when the bracket closes.</summary>
    public BlendMode Blend { get; }
}
