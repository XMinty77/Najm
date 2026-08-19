using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Backend-facing SPI: the rectangle one node's subtree is bounded by, opened as a plain saved clip
/// by <see cref="IDrawContext2D.BeginClipBracket(in ClipBracket)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This value is built by the engine's render traverser, not by authors. It is the rectangular case
/// of §6.7's <c>Clip</c>, and it exists as a bracket of its own precisely because §6.7's table says
/// <em>clip state alone does not isolate</em>: a clip has to bound the node and every descendant,
/// which a leaf-level <see cref="IDrawContext2D.PushClip(in Rect)"/> cannot do, but it must not make
/// the subtree a stacking scope, which a <see cref="UnitBracket"/> would. Carrying it on the unit
/// bracket instead would cost an offscreen no clip needs and — the part that is not merely cost —
/// would make a descendant's non-default <see cref="Node2D.Blend"/> composite against the clipped
/// unit rather than past it.
/// </para>
/// <para>
/// A node that clips <em>and</em> isolates opens both, clip outermost, because §6.7's semantic order
/// is clip, then render node and children, then composite with opacity and blend: the clip has to
/// bound what the unit's group captures rather than being applied inside it.
/// </para>
/// <para>
/// <strong>Why the clip travels with a matrix.</strong> §6.7's clip is stated in the node's own
/// local coordinates, but the traverser opens this bracket <em>before</em> it installs that node's
/// engine transform — opening a bracket sheds whatever transform was installed, so the order cannot
/// be reversed. The rectangle would therefore arrive in a space the backend has no way to name. It
/// carries <see cref="ClipToDevice"/> instead: the same <c>nodeWorld × layerBase × renderScale</c>
/// mapping the traverser is about to install, under which <see cref="Clip"/> means exactly what the
/// author wrote. Passing a device-space rectangle instead would have silently squared off every clip
/// under a rotation.
/// </para>
/// <para>
/// Only the rectangular case is expressible. Path clips and <c>Mask</c> are M2, and neither is
/// approximated here; a mask isolates in any case, so it belongs to the unit bracket rather than to
/// this one.
/// </para>
/// <para>
/// <c>default(ClipBracket)</c> is the empty rectangle under the zero matrix — a bracket that hides
/// everything inside it. The engine always constructs one explicitly.
/// </para>
/// </remarks>
public readonly record struct ClipBracket
{
    /// <summary>Creates a bracket from one node's clip rectangle and the mapping it is written in.</summary>
    /// <param name="clip">
    /// The rectangle the subtree is bounded by, expressed in the space <paramref name="clipToDevice"/>
    /// maps from. An empty rectangle is legal and hides the subtree.
    /// </param>
    /// <param name="clipToDevice">The finite mapping from <paramref name="clip"/>'s space to device pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component of <paramref name="clipToDevice"/> is not finite.
    /// </exception>
    public ClipBracket(Rect clip, in Matrix3x2 clipToDevice)
    {
        if (!IsFinite(clipToDevice))
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipToDevice),
                clipToDevice,
                "A clip bracket's clip mapping must be finite.");
        }

        Clip = clip;
        ClipToDevice = clipToDevice;
    }

    /// <summary>
    /// Gets the rectangle the bracket's contents are bounded by, in the space
    /// <see cref="ClipToDevice"/> maps from.
    /// </summary>
    /// <remarks>
    /// The clip applies to everything the subtree draws and to nothing outside it, which is the whole
    /// reason it rides a bracket rather than being pushed by each leaf. It bounds rather than
    /// composites: nothing is staged offscreen, and a descendant's own
    /// <see cref="IDrawContext2D.PushClip(in Rect)"/> composes strictly inside it.
    /// </remarks>
    public Rect Clip { get; }

    /// <summary>Gets the finite mapping from <see cref="Clip"/>'s space to device pixels.</summary>
    public Matrix3x2 ClipToDevice { get; }

    private static bool IsFinite(in Matrix3x2 matrix) =>
        float.IsFinite(matrix.M11)
        && float.IsFinite(matrix.M12)
        && float.IsFinite(matrix.M21)
        && float.IsFinite(matrix.M22)
        && float.IsFinite(matrix.M31)
        && float.IsFinite(matrix.M32);
}
