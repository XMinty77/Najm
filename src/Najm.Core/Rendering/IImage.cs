namespace Najm.Core;

/// <summary>Represents an immutable, explicitly owned image snapshot.</summary>
/// <remarks>
/// The consumer owns an image returned by an engine API and must dispose it. Holding a snapshot
/// while its source target is written can force backend copy-on-write, so callers should keep
/// snapshots only for the operation that consumes them. This interface is pre-release; its
/// contract will be completed before external package publication.
/// </remarks>
public interface IImage : IDisposable
{
    /// <summary>Gets the image dimensions in pixels.</summary>
    PixelSize Size { get; }

    /// <summary>
    /// Copies the entire image into a tightly packed, top-left-origin pixel buffer.
    /// </summary>
    /// <param name="destination">
    /// A buffer of at least <c>Width × Height × 4</c> bytes. Extra bytes are untouched.
    /// </param>
    /// <param name="format">The requested byte and alpha layout.</param>
    /// <remarks>
    /// Eight-bit readback is converted to tagged sRGB. This is an explicitly synchronous slow path
    /// for capture, export, and tests; ordinary rendering must not read pixels.
    /// </remarks>
    void CopyPixels(Span<byte> destination, PixelFormat format);
}
