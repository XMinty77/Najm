using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Resolves how a scene's virtual frame lands on an output surface: the uniform scale that fits it
/// and the device rectangle it occupies once fitted.
/// </summary>
/// <remarks>
/// <para>
/// One place states the rule so the paths that apply it cannot drift.
/// <see cref="Scene.Render(IRenderTarget)"/> derives the render scale here; a compositor places the
/// frame here; an offline run inherits both by going through the composited path. The rule itself
/// is §5.1's: <strong>fit, never stretch</strong> — the largest uniform scale that puts the whole
/// virtual frame inside the output, with the leftover on the unlimited axis left as bars.
/// </para>
/// <para>
/// <strong>Bars are not painted here, and that is deliberate.</strong> §5.1 makes bar color a host
/// concern (<c>HostOptions.BarColor</c>). An offline render has no host, so its bars stay whatever
/// the surface was cleared to, which for every path in this engine is transparent. Inventing a bar
/// color would put a decision the host owns into files the host never sees.
/// </para>
/// </remarks>
public static class FramePlacement
{
    /// <summary>
    /// Returns the virtual-to-device pixel scale for one output size: the largest uniform scale
    /// that fits the whole virtual frame inside it.
    /// </summary>
    /// <param name="virtualResolution">The scene's finite, positive virtual resolution.</param>
    /// <param name="outputSize">The output surface's pixel size.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The virtual resolution is not finite and positive, or the output size is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The pair does not yield a finite, positive scale.
    /// </exception>
    public static float ResolveRenderScale(in Vector2 virtualResolution, PixelSize outputSize)
    {
        EnsureVirtualResolution(virtualResolution);
        if (outputSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputSize),
                outputSize,
                "An output size must have positive dimensions.");
        }

        var scale = MathF.Min(
            outputSize.Width / virtualResolution.X,
            outputSize.Height / virtualResolution.Y);
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            throw new InvalidOperationException(
                $"A {outputSize.Width}×{outputSize.Height} target and a {virtualResolution.X}×" +
                $"{virtualResolution.Y} virtual resolution do not yield a finite, positive render scale.");
        }

        return scale;
    }

    /// <summary>
    /// Returns where one content extent sits inside one output extent on a single axis: centred,
    /// in whole pixels.
    /// </summary>
    /// <param name="outputExtent">The output surface's extent on this axis, in pixels.</param>
    /// <param name="contentExtent">The fitted content's extent on this axis, in pixels.</param>
    /// <remarks>
    /// <para>
    /// <strong>The rounding rule.</strong> The offset is <c>floor((output − content) / 2)</c>. When
    /// the leftover does not divide evenly the extra pixel goes to the far bar — the right one
    /// horizontally, the bottom one vertically — because a rule has to pick a side and picking the
    /// near one would shift content away from the origin on exactly the sizes where a reader is
    /// least able to tell. The choice is arbitrary; being the same choice on every axis, every
    /// path, and every frame is not. A 9-pixel output holding 4 pixels of content therefore leaves
    /// 2 pixels before it and 3 after.
    /// </para>
    /// <para>
    /// The result is clamped at zero: fitting guarantees the content is no larger than the output,
    /// but the ceiling that turns a fractional device extent into whole pixels can add one pixel on
    /// the limiting axis, and a negative offset would push content off the near edge to save a
    /// pixel at the far one.
    /// </para>
    /// </remarks>
    public static int ResolveContentOffset(int outputExtent, int contentExtent)
    {
        var leftover = outputExtent - contentExtent;
        return leftover <= 0 ? 0 : leftover / 2;
    }

    /// <summary>
    /// Returns the device-pixel rectangle the virtual frame occupies on an output surface: the
    /// fitted content size, centred.
    /// </summary>
    /// <param name="virtualResolution">The scene's finite, positive virtual resolution.</param>
    /// <param name="outputSize">The output surface's pixel size.</param>
    /// <param name="renderScale">
    /// The finite, positive scale the content is rendered at — normally
    /// <see cref="ResolveRenderScale(in Vector2, PixelSize)"/> of the same pair.
    /// </param>
    /// <remarks>
    /// Extents round outward so a fractional edge is covered rather than cropped, matching how a
    /// compositor sizes the surface it stages the frame through; the origin then follows
    /// <see cref="ResolveContentOffset(int, int)"/>. Everything outside the returned rectangle is
    /// bar.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The virtual resolution is not finite and positive, the output size is not positive, or the
    /// render scale is not finite and positive.
    /// </exception>
    public static Rect ResolveContentRect(
        in Vector2 virtualResolution,
        PixelSize outputSize,
        float renderScale)
    {
        EnsureVirtualResolution(virtualResolution);
        if (outputSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputSize),
                outputSize,
                "An output size must have positive dimensions.");
        }
        if (!float.IsFinite(renderScale) || renderScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderScale),
                renderScale,
                "A render scale must be finite and positive.");
        }

        var width = ResolveContentExtent(virtualResolution.X, renderScale, nameof(virtualResolution));
        var height = ResolveContentExtent(virtualResolution.Y, renderScale, nameof(virtualResolution));
        return new Rect(
            ResolveContentOffset(outputSize.Width, width),
            ResolveContentOffset(outputSize.Height, height),
            width,
            height);
    }

    private static int ResolveContentExtent(float virtualExtent, float renderScale, string parameterName)
    {
        var ceiling = MathF.Ceiling(virtualExtent * renderScale);
        if (!float.IsFinite(ceiling) || ceiling > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                virtualExtent,
                "A device extent must be finite and representable as a pixel count.");
        }

        return ceiling < 1f ? 1 : (int)ceiling;
    }

    private static void EnsureVirtualResolution(in Vector2 virtualResolution)
    {
        if (!float.IsFinite(virtualResolution.X) ||
            !float.IsFinite(virtualResolution.Y) ||
            virtualResolution.X <= 0f ||
            virtualResolution.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(virtualResolution),
                virtualResolution,
                "A virtual resolution must have finite, positive components.");
        }
    }
}
