using SkiaSharp;

namespace Najm.Skia;

/// <summary>
/// The in-assembly seam a <see cref="SkiaDrawContext2D"/> draws an <see cref="Najm.Core.IImage"/>
/// through.
/// </summary>
/// <remarks>
/// <para>
/// It exists because <see cref="Najm.Core.IImage"/> now has two kinds, not one. DEVIATIONS entry 9
/// admits an <em>externally owned</em> image alongside the immutable engine snapshot: the caller
/// guarantees the pixels are stable for the duration of a draw rather than forever, which is what an
/// author re-rendering into their own GL texture every frame can actually promise. Both kinds are
/// drawn identically once lowered, so the context asks for the native image and nothing else.
/// </para>
/// <para>
/// Deliberately internal. The two implementations are <see cref="SkiaImage"/> and
/// <see cref="GlTextureImage"/>, both in this assembly, and neither
/// <see cref="Najm.Core.IImage"/> nor any public signature changes to accommodate them.
/// </para>
/// </remarks>
internal interface ISkiaNativeImage
{
    /// <summary>Gets the live native image, throwing if this wrapper has been disposed.</summary>
    SKImage GetNativeImage();
}
