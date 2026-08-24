namespace Najm.Core.Text;

/// <summary>An immutable, backend-neutral handle to one face inside one font file.</summary>
/// <remarks>
/// <para>
/// NAJM-TEXT I.2. A face handle is <strong>size-independent</strong>: sizes belong to typeset and
/// draw requests, not to the thing being drawn with. That is what lets one shaped-run cache entry
/// serve every size a label is ever animated through (II.5).
/// </para>
/// <para>
/// <strong>The retained bytes are the portability floor.</strong> No native realization is ever
/// stored here — <c>Najm.Text</c> keeps HarfBuzz face and font side tables keyed by this handle, and
/// a backend independently keeps its own typeface side table keyed by the same handle. Neither can
/// see the other's, both are environment-lifetime, and each disposes what it made. A mutable
/// <c>object</c> slot on the handle would have made the handle the coupling point between two
/// modules that must not know about each other.
/// </para>
/// <para>
/// Instances are compared by reference, and a cache keyed by a face is keyed by handle identity. A
/// family therefore hands out one handle per face and every consumer shares it.
/// </para>
/// </remarks>
public sealed class FontFace
{
    /// <summary>Creates a face handle over retained font bytes.</summary>
    /// <param name="sourceId">
    /// A stable, human-readable identifier for where the bytes came from — a file name, a resource
    /// name, an asset key. It appears in diagnostics and fail-loud messages and takes no part in
    /// matching.
    /// </param>
    /// <param name="bytes">
    /// The complete font file. The memory is retained, not copied, so the caller must not mutate it.
    /// </param>
    /// <param name="faceIndex">The zero-based face index inside a collection file. The default is 0.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceId"/> is whitespace, or <paramref name="bytes"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    public FontFace(string sourceId, ReadOnlyMemory<byte> bytes, int faceIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (bytes.IsEmpty)
        {
            throw new ArgumentException("A font face needs its font bytes.", nameof(bytes));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);

        SourceId = sourceId;
        Bytes = bytes;
        FaceIndex = faceIndex;
    }

    /// <summary>Gets the stable identifier of where these bytes came from.</summary>
    public string SourceId { get; }

    /// <summary>Gets the complete, retained font file.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Gets the zero-based face index inside a font collection.</summary>
    public int FaceIndex { get; }

    /// <inheritdoc />
    public override string ToString() => FaceIndex == 0 ? SourceId : $"{SourceId}#{FaceIndex}";
}
