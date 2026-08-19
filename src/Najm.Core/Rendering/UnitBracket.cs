using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Backend-facing SPI: the composition a single node's subtree contributes as one unit, opened as a
/// group by <see cref="IDrawContext2D.BeginUnitBracket(in UnitBracket)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This value is built by the engine's render traverser, not by authors. It is the M1 ancestor of
/// the reference's full composition SPI: <c>BeginUnit(in UnitParams { boundsHint, opacity, blend,
/// effect })</c> / <c>EndUnit()</c>. Two of those four parameters exist here — plus the rectangular
/// case of §6.7's <c>Clip</c> — and the omissions are deliberate rather than defaulted. There is no
/// <c>boundsHint</c> because M1 brackets conservatively (see
/// <see cref="IDrawContext2D.BeginUnitBracket(in UnitBracket)"/>), and no <c>effect</c> because
/// <c>Effect</c>, <c>Mask</c>, and <c>Backdrop</c> are M2 and must not be approximated. When those
/// arrive this struct grows fields and gains its reference name; the shape is chosen so that growth
/// is additive.
/// </para>
/// <para>
/// <strong>Why the clip travels with a matrix.</strong> §6.7's clip is stated in the node's own
/// local coordinates, but the traverser opens this bracket <em>before</em> it installs that node's
/// engine transform — opening a bracket sheds whatever transform was installed, so the order cannot
/// be reversed. The rectangle would therefore arrive in a space the backend has no way to name. It
/// carries <see cref="ClipToDevice"/> instead: the same <c>nodeWorld × layerBase × renderScale</c>
/// mapping the traverser is about to install, under which
/// <see cref="Clip"/> means exactly what the author wrote. Passing a device-space rectangle instead
/// would have silently squared off every clip under a rotation.
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
/// Every member is a group operation: each applies once to everything the unit's subtree drew,
/// never to each primitive inside it. That distinction is the whole reason the bracket exists — two
/// overlapping children under <c>Opacity = 0.5</c> must show the half-alpha composite of their
/// union, not two independently halved shapes, and those differ wherever they overlap.
/// </para>
/// <para>
/// <c>default(UnitBracket)</c> is the unit that contributes nothing: zero opacity over
/// <see cref="BlendMode.SrcOver"/>, clipping nothing. The engine always constructs one explicitly.
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
        : this(opacity, blend, clip: null, default)
    {
    }

    /// <summary>Creates a bracket from one node's composition state, including its clip.</summary>
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
    /// <param name="clip">
    /// The rectangle the unit's contents are clipped to, expressed in the space
    /// <paramref name="clipToDevice"/> maps from, or null to clip nothing.
    /// </param>
    /// <param name="clipToDevice">
    /// The finite mapping from <paramref name="clip"/>'s space to device pixels. It is ignored, and
    /// need not be meaningful, when <paramref name="clip"/> is null.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="opacity"/> is not finite and within [0, 1], or <paramref name="clip"/> is
    /// set and a component of <paramref name="clipToDevice"/> is not finite.
    /// </exception>
    public UnitBracket(float opacity, BlendMode blend, Rect? clip, in Matrix3x2 clipToDevice)
    {
        if (!float.IsFinite(opacity) || opacity < 0f || opacity > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity),
                opacity,
                "A unit bracket's opacity must be finite and between zero and one inclusive.");
        }
        if (clip is not null && !IsFinite(clipToDevice))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipToDevice),
                clipToDevice,
                "A unit bracket's clip mapping must be finite.");
        }

        Opacity = opacity;
        Blend = blend;
        Clip = clip;
        // Zeroed rather than set to the identity when there is no clip, so that two brackets with
        // the same opacity and blend compare equal however the traverser happened to reach them,
        // and so that default(UnitBracket) stays exactly the bracket that contributes nothing.
        ClipToDevice = clip is null ? default : clipToDevice;
    }

    /// <summary>Gets the finite group alpha in [0, 1] applied when the bracket closes.</summary>
    public float Opacity { get; }

    /// <summary>Gets the blend the unit's contents are composited with when the bracket closes.</summary>
    public BlendMode Blend { get; }

    /// <summary>
    /// Gets the rectangle the unit's contents are clipped to, in the space
    /// <see cref="ClipToDevice"/> maps from, or null when the unit clips nothing.
    /// </summary>
    /// <remarks>
    /// The clip applies to everything the unit's subtree draws and to nothing outside it, which is
    /// the whole reason it rides the bracket rather than being pushed by each leaf. It is a group
    /// operation in the same sense <see cref="Opacity"/> is: it bounds the unit's contents once,
    /// before the composite, and a descendant cannot push its way back out of it.
    /// </remarks>
    public Rect? Clip { get; }

    /// <summary>
    /// Gets the mapping from <see cref="Clip"/>'s space to device pixels. It is the zero matrix,
    /// and carries no meaning, when <see cref="Clip"/> is null.
    /// </summary>
    public Matrix3x2 ClipToDevice { get; }

    private static bool IsFinite(in Matrix3x2 matrix) =>
        float.IsFinite(matrix.M11)
        && float.IsFinite(matrix.M12)
        && float.IsFinite(matrix.M21)
        && float.IsFinite(matrix.M22)
        && float.IsFinite(matrix.M31)
        && float.IsFinite(matrix.M32);
}
