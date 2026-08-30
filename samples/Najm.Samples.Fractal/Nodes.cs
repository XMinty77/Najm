using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Fractal;

/// <summary>
/// The fractal itself: an ordinary <see cref="Drawable"/> that happens to draw an image the author's
/// own GL pipeline rendered.
/// </summary>
/// <remarks>
/// This is the whole claim of ARCHITECTURE §7.5 made concrete — a custom GL pipeline is "an ordinary
/// drawable that owns its render-to-texture privately". Nothing below this line is GPU-aware except
/// the capability check, and that check is now a backstop rather than the contract: the scene
/// refuses at load, where an author can act on it, because <c>Env.Caps</c> finally answers there
/// (F-3, closed). This one stays because a drawable can be moved into a tree the scene did not
/// validate, and transparent black is not a failure anyone would notice.
/// </remarks>
internal sealed class FractalNode(FractalTexture texture) : Drawable
{
    /// <inheritdoc />
    public override Rect VisualBounds => new(0f, 0f, Design.Frame.Width, Design.Frame.Height);

    /// <inheritdoc />
    public override void Render(IDrawContext2D context)
    {
        if (!context.Caps.HasFlag(RenderCaps.GpuBacked))
        {
            throw new InvalidOperationException(
                "This drawable samples an externally owned GL texture and needs a GPU-backed "
                + $"target; this one advertises {context.Caps}. Render through "
                + "GpuSkiaSurfaceProvider, not the raster provider.");
        }

        // The transform is derived from the image's own size rather than assumed, because the wrap
        // reports the texture's current extent and an author who reallocates gets the right frame
        // for free. Verified by reallocating every third frame; see NOTES.md F-15.
        var image = texture.Acquire();
        var fit = new Vector2(
            Design.Frame.Width / (float)image.Size.Width,
            Design.Frame.Height / (float)image.Size.Height);

        // One texel to one virtual unit is the case this clip runs in, and nearest is exact there.
        // Anything else is a real resample and wants linear.
        var sampling = fit == Vector2.One ? ImageSampling.Nearest : ImageSampling.Linear;
        context.DrawImage(image, Matrix3x2.CreateScale(fit.X, fit.Y), sampling);
    }
}

/// <summary>
/// An elliptical vignette, drawn by the engine over the shader's output.
/// </summary>
/// <remarks>
/// Present partly because the frame wants it and partly because it proves the point: the wrapped
/// texture is a normal image in a normal layer, and ordinary portable drawables composite over it
/// with no special handling. The node carries the ellipse in its own transform, so the gradient
/// stays a circle in local units and the aspect lives where aspect belongs.
/// </remarks>
internal sealed class VignetteNode : Node2D
{
    private const float LocalRadius = 1400f;

    private readonly Paint paint;

    public VignetteNode()
    {
        Position = new Vector2(Design.Frame.Width * 0.5f, Design.Frame.Height * 0.5f);
        Scale = new Vector2(1f, (float)Design.Frame.Height / Design.Frame.Width);

        // One RGB throughout, only alpha ramping — the falloff darkens without tinting.
        var shade = Color.Srgb(0.008f, 0.010f, 0.022f);
        paint = Paint.Fill(Brush.Radial(
            Vector2.Zero,
            LocalRadius,
            [
                new GradientStop(0.00f, shade.WithAlpha(0f)),
                new GradientStop(0.42f, shade.WithAlpha(0.015f)),
                new GradientStop(0.70f, shade.WithAlpha(0.14f)),
                new GradientStop(0.88f, shade.WithAlpha(0.38f)),
                new GradientStop(1.00f, shade.WithAlpha(0.66f)),
            ]));
    }

    /// <inheritdoc />
    public override Rect VisualBounds => new(-LocalRadius, -LocalRadius, LocalRadius * 2f, LocalRadius * 2f);

    /// <inheritdoc />
    public override void Render(IDrawContext2D context) =>
        context.DrawCircle(Vector2.Zero, LocalRadius, paint);
}

/// <summary>
/// A restrained lower-left instrument: magnification on a logarithmic rule, and the live iteration
/// limit as a bar above it.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the iteration limit is a <em>subject</em> of this clip and the eye needs
/// somewhere to confirm what it is seeing when the filaments dissolve. It is deliberately quiet:
/// no text (the offline environment's typesetter is the fail-loud null one, by design), one hairline
/// rule, decade ticks, a travelling marker, and one warm bar.
/// </para>
/// <para>
/// Everything here is portable Tier-1/Tier-2 drawing. If the instrument were the only content, this
/// scene would run unchanged on the raster provider.
/// </para>
/// </remarks>
internal sealed class InstrumentNode(Func<InstrumentReading> read) : Drawable
{
    private const float X0 = 108f;
    private const float X1 = 468f;
    private const float RuleY = 1000f;
    private const float BarY = 972f;

    private readonly Paint scrim = Paint.Fill(Brush.Radial(
        new Vector2(X0 + 40f, RuleY),
        620f,
        [
            new GradientStop(0.00f, Design.Background.WithAlpha(0.80f)),
            new GradientStop(0.45f, Design.Background.WithAlpha(0.52f)),
            new GradientStop(0.78f, Design.Background.WithAlpha(0.16f)),
            new GradientStop(1.00f, Design.Background.WithAlpha(0f)),
        ]));

    /// <inheritdoc />
    public override Rect VisualBounds => new(X0 - 560f, BarY - 620f, 1240f, 1240f);

    /// <inheritdoc />
    public override void Render(IDrawContext2D context)
    {
        var reading = read();
        var ink = Design.InstrumentInk;

        // A soft scrim first, or the hairlines land on whatever the shader happened to put there and
        // read as a scratch. It is a disc rather than a bar so it has no edge of its own.
        context.DrawCircle(new Vector2(X0 + 40f, RuleY), 620f, scrim);

        // The rule, and a decade tick for every power of ten of magnification it spans.
        context.DrawLine(
            new Vector2(X0, RuleY),
            new Vector2(X1, RuleY),
            Paint.Stroke(ink.WithAlpha(0.24f), 1f));

        for (var decade = 0; decade <= reading.Decades; decade++)
        {
            var x = X0 + ((X1 - X0) * decade / reading.Decades);
            var tall = decade % 2 == 0;
            context.DrawLine(
                new Vector2(x, RuleY),
                new Vector2(x, RuleY + (tall ? 8f : 4f)),
                Paint.Stroke(ink.WithAlpha(tall ? 0.34f : 0.20f), 1f));
        }

        // The travelling marker: where in that span the frame currently sits.
        var marker = new Vector2(X0 + ((X1 - X0) * Math.Clamp(reading.DepthFraction, 0f, 1f)), RuleY);
        context.DrawCircle(
            marker,
            11f,
            Paint.Fill(Brush.RadialFade(marker, 11f, ink.WithAlpha(0.30f)), blendMode: BlendMode.Plus));
        context.DrawCircle(marker, 3f, Paint.Fill(ink.WithAlpha(0.95f)));

        // The iteration bar. Logarithmic, because the difference between 100 and 200 iterations is
        // the interesting one and the difference between 1900 and 2000 is not.
        var accent = Design.InstrumentAccent;
        var barWidth = (X1 - X0) * Math.Clamp(reading.IterationFraction, 0f, 1f);
        context.DrawLine(
            new Vector2(X0, BarY),
            new Vector2(X1, BarY),
            Paint.Stroke(ink.WithAlpha(0.14f), 4f, cap: LineCap.Round));

        if (barWidth > 2f)
        {
            var head = new Vector2(X0 + barWidth, BarY);
            context.DrawLine(new Vector2(X0, BarY), head, Paint.Stroke(accent.WithAlpha(0.85f), 4f, cap: LineCap.Round));
            context.DrawCircle(
                head,
                17f,
                Paint.Fill(Brush.RadialFade(head, 17f, accent.WithAlpha(0.34f)), blendMode: BlendMode.Plus));
        }
    }
}

/// <summary>What the instrument is told each frame, in already-normalized terms.</summary>
internal readonly record struct InstrumentReading
{
    /// <summary>Gets how many decades of magnification the rule spans.</summary>
    public required int Decades { get; init; }

    /// <summary>Gets where in that span the current frame sits, in [0, 1].</summary>
    public required float DepthFraction { get; init; }

    /// <summary>Gets the iteration limit's logarithmic position in [0, 1].</summary>
    public required float IterationFraction { get; init; }
}
