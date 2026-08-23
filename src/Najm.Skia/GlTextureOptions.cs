using SkiaSharp;
using CoreColorSpace = Najm.Core.ColorSpace;

namespace Najm.Skia;

/// <summary>States which corner of a GL texture's memory holds its first row.</summary>
/// <remarks>
/// GL puts the origin at the bottom left: row zero of a texture rendered through a framebuffer
/// object is the <em>bottom</em> row of what the fragment shader drew. Skia's images are top-left by
/// default. Getting this wrong is not subtle — the image appears vertically flipped — but it is also
/// not something the wrap can detect, so the author states it.
/// </remarks>
public enum GlTextureOrigin
{
    /// <summary>Row zero is the top row, as an uploaded image ordinarily is.</summary>
    TopLeft,

    /// <summary>
    /// Row zero is the bottom row, as it is for a texture an author rendered into through a
    /// framebuffer object. This is the usual answer for render-to-texture content.
    /// </summary>
    BottomLeft,
}

/// <summary>Describes how an externally owned GL texture is to be interpreted when wrapped.</summary>
/// <remarks>
/// Every member has a meaningful zero, so <c>default</c> is the common case: a
/// <c>GL_TEXTURE_2D</c>, <c>GL_RGBA8</c>, top-left, premultiplied, sRGB-tagged texture. An author
/// states only what differs, for example
/// <c>new GlTextureOptions { Origin = GlTextureOrigin.BottomLeft }</c> for render-to-texture output.
/// </remarks>
public readonly record struct GlTextureOptions
{
    /// <summary><c>GL_TEXTURE_2D</c>.</summary>
    public const uint Texture2D = 0x0DE1;

    /// <summary><c>GL_RGBA8</c>.</summary>
    public const uint Rgba8 = 0x8058;

    /// <summary><c>GL_SRGB8_ALPHA8</c>.</summary>
    public const uint Srgb8Alpha8 = 0x8C43;

    /// <summary><c>GL_RGBA16F</c>.</summary>
    public const uint Rgba16f = 0x881A;

    /// <summary>Gets which corner holds the texture's first row. Defaults to <see cref="GlTextureOrigin.TopLeft"/>.</summary>
    public GlTextureOrigin Origin { get; init; }

    /// <summary>Gets the color-space tag the texture's contents carry. Defaults to <see cref="CoreColorSpace.Srgb"/>.</summary>
    public CoreColorSpace ColorSpace { get; init; }

    /// <summary>
    /// Gets whether the texture's color channels are <em>not</em> premultiplied by its alpha.
    /// Defaults to false, because everything past Najm's rendering boundary is premultiplied.
    /// </summary>
    public bool IsStraightAlpha { get; init; }

    /// <summary>Gets the GL texture target, or zero for <see cref="Texture2D"/>.</summary>
    public uint TextureTarget { get; init; }

    /// <summary>Gets the GL sized internal format, or zero for <see cref="Rgba8"/>.</summary>
    /// <remarks>
    /// One of <see cref="Rgba8"/>, <see cref="Srgb8Alpha8"/>, or <see cref="Rgba16f"/>. This is the
    /// texture's <em>storage</em>; <see cref="ColorSpace"/> is the tag its contents are read with,
    /// and the two are independent — a linear-light pipeline is <see cref="Rgba16f"/> plus
    /// <see cref="CoreColorSpace.LinearSrgb"/>.
    /// </remarks>
    public uint SizedFormat { get; init; }

    /// <summary>Gets <see cref="TextureTarget"/> with its zero default resolved.</summary>
    public uint ResolvedTextureTarget => TextureTarget == 0 ? Texture2D : TextureTarget;

    /// <summary>Gets <see cref="SizedFormat"/> with its zero default resolved.</summary>
    public uint ResolvedSizedFormat => SizedFormat == 0 ? Rgba8 : SizedFormat;

    /// <summary>Validates the combination and throws with a message naming what is wrong.</summary>
    /// <param name="parameterName">The caller's parameter name, for a faithful exception.</param>
    /// <exception cref="ArgumentException">A member is not a defined or supported value.</exception>
    internal void Validate(string parameterName)
    {
        if (!Enum.IsDefined(Origin))
        {
            throw new ArgumentException("The GL texture origin is not defined.", parameterName);
        }

        if (!Enum.IsDefined(ColorSpace))
        {
            throw new ArgumentException("The color-space tag is not defined.", parameterName);
        }

        if (ResolvedTextureTarget != Texture2D)
        {
            throw new ArgumentException(
                $"Only GL_TEXTURE_2D (0x{Texture2D:X4}) textures can be wrapped; "
                + $"0x{ResolvedTextureTarget:X4} was requested.",
                parameterName);
        }

        _ = ResolveColorType(parameterName);
    }

    /// <summary>Maps the texture's sized storage format onto the Skia color type that reads it.</summary>
    /// <param name="parameterName">The caller's parameter name, for a faithful exception.</param>
    /// <exception cref="ArgumentException">The sized format has no supported Skia color type.</exception>
    internal SKColorType ResolveColorType(string parameterName) =>
        ResolvedSizedFormat switch
        {
            Rgba8 or Srgb8Alpha8 => SKColorType.Rgba8888,
            Rgba16f => SKColorType.RgbaF16,
            _ => throw new ArgumentException(
                $"GL sized format 0x{ResolvedSizedFormat:X4} is not a supported wrap format; use "
                + $"GL_RGBA8 (0x{Rgba8:X4}), GL_SRGB8_ALPHA8 (0x{Srgb8Alpha8:X4}), or GL_RGBA16F "
                + $"(0x{Rgba16f:X4}).",
                parameterName),
        };
}
