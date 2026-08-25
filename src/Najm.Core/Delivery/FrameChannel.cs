namespace Najm.Core;

/// <summary>One 8-bit per-pixel quantity that <see cref="FrameStats"/> keeps a histogram for.</summary>
/// <remarks>
/// <para>
/// Every member is a value in <c>[0, 255]</c> derived from one pixel, so every member's
/// distribution fits in the same 256-bucket histogram and every query — percentile, mean, count at
/// or above a level — is the same code. <see cref="Red"/>, <see cref="Green"/>, and
/// <see cref="Blue"/> are logical channels: they name the colour, never the byte offset, so they
/// mean the same thing whether the frame is <see cref="PixelFormat.Rgba8888"/> or
/// <see cref="PixelFormat.Bgra8888Premul"/>.
/// </para>
/// <para>
/// <b><see cref="ChannelFloor"/> and <see cref="ChannelCeiling"/> exist to make clipping a
/// lookup.</b> "How many pixels are white?" is not answerable from the three colour histograms —
/// they are marginal distributions, and a pixel at <c>(255, 0, 0)</c> contributes to red's top
/// bucket without being white. Reducing each pixel to <c>min(R, G, B)</c> and <c>max(R, G, B)</c>
/// before binning keeps the joint answer: pixels with every channel at or above <c>L</c> are
/// exactly the pixels whose floor is at or above <c>L</c>, and that holds for every <c>L</c> at
/// once, so the clipping threshold stays a query parameter rather than a measurement parameter.
/// This matters because the useful threshold is not always 255 — a shader with output dither puts a
/// rim of pixels at 254 that are white to the eye and to a codec.
/// </para>
/// </remarks>
public enum FrameChannel
{
    /// <summary>The red channel, as stored.</summary>
    Red,

    /// <summary>The green channel, as stored.</summary>
    Green,

    /// <summary>The blue channel, as stored.</summary>
    Blue,

    /// <summary>The alpha channel.</summary>
    Alpha,

    /// <summary>
    /// Rec. 709 luma <c>Y′ = 0.2126 R′ + 0.7152 G′ + 0.0722 B′</c>, computed on the sRGB-encoded
    /// bytes and rounded to the nearest level. This is a display-referred brightness code, not a
    /// photometric quantity — see <see cref="FrameStats.MeanRelativeLuminance"/> for that.
    /// </summary>
    Luma,

    /// <summary>
    /// <c>min(R, G, B)</c>. Its count at or above <c>L</c> is the number of pixels whose every
    /// colour channel has reached <c>L</c> — the clipped-to-white population.
    /// </summary>
    ChannelFloor,

    /// <summary>
    /// <c>max(R, G, B)</c>. Its count at or below <c>L</c> is the number of pixels whose every
    /// colour channel is still at or under <c>L</c> — the crushed-to-black population.
    /// </summary>
    ChannelCeiling,
}
