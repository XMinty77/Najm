using Najm.Utils;

namespace Najm.Core;

/// <summary>A backend-neutral value describing a path fill or stroke.</summary>
/// <remarks>
/// <c>default(Paint)</c> is intentionally a transparent, antialiased fill and therefore draws
/// nothing under source-over compositing. Its zero stroke width is ignored because its style is
/// <see cref="PaintStyle.Fill"/>; explicitly constructed stroke paints still require positive width.
/// </remarks>
public readonly struct Paint
{
    private readonly bool antialiasDisabled;

    /// <summary>Creates a path paint.</summary>
    /// <param name="color">The sRGB-referenced color.</param>
    /// <param name="style">Whether to fill or stroke.</param>
    /// <param name="strokeWidth">The positive local-unit width required for a stroke.</param>
    /// <param name="isAntialias">Whether edge antialiasing is enabled.</param>
    /// <param name="blendMode">The portable source-to-destination blend operation.</param>
    public Paint(
        Color color,
        PaintStyle style = PaintStyle.Fill,
        float strokeWidth = 1f,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver)
    {
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentException("The paint style is not defined.", nameof(style));
        }
        if (!float.IsFinite(strokeWidth) || strokeWidth < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth), "Stroke width must be finite and nonnegative.");
        }
        if (style == PaintStyle.Stroke && strokeWidth == 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth), "Stroke paints require a positive width.");
        }
        if (!Enum.IsDefined(blendMode))
        {
            throw new ArgumentException("The blend mode is not defined.", nameof(blendMode));
        }

        Color = color;
        Style = style;
        StrokeWidth = strokeWidth;
        BlendMode = blendMode;
        antialiasDisabled = !isAntialias;
    }

    /// <summary>Gets the sRGB-referenced color.</summary>
    public Color Color { get; }

    /// <summary>Gets whether geometry is filled or stroked.</summary>
    public PaintStyle Style { get; }

    /// <summary>
    /// Gets the stroke width in local units. It is ignored by fill paints, including the zero width
    /// of <c>default(Paint)</c>.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>Gets whether edge antialiasing is enabled. It is true for <c>default(Paint)</c>.</summary>
    public bool IsAntialias => !antialiasDisabled;

    /// <summary>Gets the portable source-to-destination blend operation.</summary>
    public BlendMode BlendMode { get; }

    /// <summary>Creates a fill paint.</summary>
    public static Paint Fill(
        Color color,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver) =>
        new(color, PaintStyle.Fill, strokeWidth: 1f, isAntialias, blendMode);

    /// <summary>Creates a stroke paint.</summary>
    public static Paint Stroke(
        Color color,
        float width,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver) =>
        new(color, PaintStyle.Stroke, width, isAntialias, blendMode);
}
