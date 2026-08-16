using Najm.Utils;

namespace Najm.Core;

/// <summary>A backend-neutral value describing a path fill or stroke.</summary>
/// <remarks>
/// <c>default(Paint)</c> is intentionally a transparent, antialiased fill and therefore draws
/// nothing under source-over compositing. Its zero stroke width is ignored because its style is
/// <see cref="PaintStyle.Fill"/>; explicitly constructed stroke paints still require positive width.
/// The stroke geometry members keep their documented defaults in that zero value: butt caps, miter
/// joins, a miter limit of <see cref="DefaultMiterLimit"/>, no dash, and no brush.
/// </remarks>
public readonly struct Paint
{
    /// <summary>The miter limit a paint uses unless one is supplied.</summary>
    public const float DefaultMiterLimit = 4f;

    private readonly bool antialiasDisabled;
    private readonly float miterLimitOffset;

    /// <summary>Creates a path paint that paints one flat color.</summary>
    /// <param name="color">The sRGB-referenced color.</param>
    /// <param name="style">Whether to fill or stroke.</param>
    /// <param name="strokeWidth">The positive local-unit width required for a stroke.</param>
    /// <param name="isAntialias">Whether edge antialiasing is enabled.</param>
    /// <param name="blendMode">The portable source-to-destination blend operation.</param>
    /// <param name="cap">The geometry added at an open contour's ends.</param>
    /// <param name="join">The geometry added where two stroked segments meet.</param>
    /// <param name="miterLimit">The finite miter cutoff, at least one.</param>
    /// <param name="dash">The stroke's dash pattern, or null for a solid stroke.</param>
    public Paint(
        Color color,
        PaintStyle style = PaintStyle.Fill,
        float strokeWidth = 1f,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver,
        LineCap cap = LineCap.Butt,
        LineJoin join = LineJoin.Miter,
        float miterLimit = DefaultMiterLimit,
        StrokeDash? dash = null)
        : this(brush: null, color, style, strokeWidth, isAntialias, blendMode, cap, join, miterLimit, dash)
    {
    }

    /// <summary>Creates a path paint that paints a brush.</summary>
    /// <param name="brush">The solid, gradient, or pattern brush.</param>
    /// <param name="style">Whether to fill or stroke.</param>
    /// <param name="strokeWidth">The positive local-unit width required for a stroke.</param>
    /// <param name="isAntialias">Whether edge antialiasing is enabled.</param>
    /// <param name="blendMode">The portable source-to-destination blend operation.</param>
    /// <param name="cap">The geometry added at an open contour's ends.</param>
    /// <param name="join">The geometry added where two stroked segments meet.</param>
    /// <param name="miterLimit">The finite miter cutoff, at least one.</param>
    /// <param name="dash">The stroke's dash pattern, or null for a solid stroke.</param>
    public Paint(
        Brush brush,
        PaintStyle style = PaintStyle.Fill,
        float strokeWidth = 1f,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver,
        LineCap cap = LineCap.Butt,
        LineJoin join = LineJoin.Miter,
        float miterLimit = DefaultMiterLimit,
        StrokeDash? dash = null)
        : this(
            brush,
            brush.Kind == BrushKind.Solid ? brush.Color : Color.White,
            style,
            strokeWidth,
            isAntialias,
            blendMode,
            cap,
            join,
            miterLimit,
            dash)
    {
    }

    private Paint(
        Brush? brush,
        Color color,
        PaintStyle style,
        float strokeWidth,
        bool isAntialias,
        BlendMode blendMode,
        LineCap cap,
        LineJoin join,
        float miterLimit,
        StrokeDash? dash)
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
        if (!Enum.IsDefined(cap))
        {
            throw new ArgumentException("The line cap is not defined.", nameof(cap));
        }
        if (!Enum.IsDefined(join))
        {
            throw new ArgumentException("The line join is not defined.", nameof(join));
        }
        if (!float.IsFinite(miterLimit) || miterLimit < 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(miterLimit),
                miterLimit,
                "The miter limit must be finite and at least one.");
        }
        if (dash is { IsEmpty: true })
        {
            throw new ArgumentException(
                "A dash pattern must carry intervals; default(StrokeDash) cannot dash a stroke.",
                nameof(dash));
        }
        if (brush is { Kind: BrushKind.ImagePattern, Image: null })
        {
            throw new ArgumentException("An image pattern brush requires an image.", nameof(brush));
        }

        Brush = brush;
        Color = color;
        Style = style;
        StrokeWidth = strokeWidth;
        BlendMode = blendMode;
        Cap = cap;
        Join = join;
        Dash = dash;
        antialiasDisabled = !isAntialias;
        miterLimitOffset = miterLimit - DefaultMiterLimit;
    }

    /// <summary>
    /// Gets the brush painting the geometry, or null when the flat <see cref="Color"/> is painted
    /// directly. It is null for <c>default(Paint)</c>.
    /// </summary>
    public Brush? Brush { get; }

    /// <summary>
    /// Gets the sRGB-referenced color. A brush-built paint reports its solid brush's color, or
    /// opaque white for a gradient or pattern, whose color the brush supplies and whose output a
    /// backend modulates by this alpha.
    /// </summary>
    public Color Color { get; }

    /// <summary>Gets whether geometry is filled or stroked.</summary>
    public PaintStyle Style { get; }

    /// <summary>
    /// Gets the stroke width in local units. It is ignored by fill paints, including the zero width
    /// of <c>default(Paint)</c>.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>Gets the geometry added at an open contour's ends. It is butt for <c>default(Paint)</c>.</summary>
    public LineCap Cap { get; }

    /// <summary>Gets the geometry added where two stroked segments meet. It is miter for <c>default(Paint)</c>.</summary>
    public LineJoin Join { get; }

    /// <summary>
    /// Gets the miter cutoff, at least one. It is <see cref="DefaultMiterLimit"/> for
    /// <c>default(Paint)</c>.
    /// </summary>
    public float MiterLimit => DefaultMiterLimit + miterLimitOffset;

    /// <summary>
    /// Gets the stroke's dash pattern in local units, or null for a solid stroke. It is null for
    /// <c>default(Paint)</c>.
    /// </summary>
    public StrokeDash? Dash { get; }

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

    /// <summary>Creates a fill paint that paints a brush.</summary>
    public static Paint Fill(
        Brush brush,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver) =>
        new(brush, PaintStyle.Fill, strokeWidth: 1f, isAntialias, blendMode);

    /// <summary>Creates a stroke paint.</summary>
    public static Paint Stroke(
        Color color,
        float width,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver,
        LineCap cap = LineCap.Butt,
        LineJoin join = LineJoin.Miter,
        float miterLimit = DefaultMiterLimit,
        StrokeDash? dash = null) =>
        new(color, PaintStyle.Stroke, width, isAntialias, blendMode, cap, join, miterLimit, dash);

    /// <summary>Creates a stroke paint that paints a brush.</summary>
    public static Paint Stroke(
        Brush brush,
        float width,
        bool isAntialias = true,
        BlendMode blendMode = BlendMode.SrcOver,
        LineCap cap = LineCap.Butt,
        LineJoin join = LineJoin.Miter,
        float miterLimit = DefaultMiterLimit,
        StrokeDash? dash = null) =>
        new(brush, PaintStyle.Stroke, width, isAntialias, blendMode, cap, join, miterLimit, dash);
}
