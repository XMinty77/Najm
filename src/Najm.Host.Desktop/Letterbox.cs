using System.Numerics;
using Najm.Core;

namespace Najm.Host.Desktop;

/// <summary>
/// Where a scene's virtual frame lands on one window framebuffer, and the inverse of that mapping.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is ARCHITECTURE §5.1's "single scaling point", made into one value.</strong> The
/// section says a host "letterboxes virtual→output preserving aspect and inverse-maps input", and
/// that "one scaling point serves rendering, pointer math, and embedding identically". The failure
/// it is written against is specific and common: the picture is right and the clicks land somewhere
/// else, because two pieces of a host each worked the geometry out for themselves and one of them
/// forgot the bars. So the geometry is worked out once, here, and both halves read the same value.
/// </para>
/// <para>
/// <strong>The forward half is not this type's opinion.</strong> <see cref="Resolve"/> asks
/// <see cref="FramePlacement"/> — the Core type <see cref="Scene.Render(IRenderTarget)"/> and the
/// compositor already place frames through — for the scale and the content rectangle. That is what
/// makes <see cref="ToVirtual"/> a true inverse rather than a second implementation that agrees on
/// the cases somebody tested: the compositor installs a uniform <see cref="RenderScale"/> and
/// offsets the frame to <see cref="ContentRect"/>'s origin, and this undoes exactly those two
/// steps, in that order.
/// </para>
/// <para>
/// <strong>Everything here is device pixels.</strong> §3.3 calls that space "Window — physical
/// pixels; exists only inside hosts". A platform that reports pointer positions in logical units
/// converts to device pixels before asking this type anything; see
/// <see cref="DesktopHost"/>, which does that conversion and says why.
/// </para>
/// <para>
/// <strong>The inverse is deliberately unclamped.</strong> §9.1: "Pointer coordinates outside the
/// letterbox map linearly and are delivered unclamped — virtual coordinates may be negative or
/// exceed <c>VirtualResolution</c>". A drag that leaves the window keeps producing sensible
/// coordinates, which is what stops a dragged handle sticking to the edge; an author who wants
/// containment tests bounds.
/// </para>
/// </remarks>
public readonly record struct Letterbox
{
    private readonly Vector2 contentOrigin;

    private Letterbox(PixelSize outputSize, float renderScale, Rect contentRect)
    {
        OutputSize = outputSize;
        RenderScale = renderScale;
        ContentRect = contentRect;
        contentOrigin = new Vector2(contentRect.X, contentRect.Y);
    }

    /// <summary>Gets the framebuffer this mapping was resolved against, in device pixels.</summary>
    public PixelSize OutputSize { get; }

    /// <summary>Gets the uniform virtual→device scale the frame is drawn at.</summary>
    /// <remarks>
    /// The same number <see cref="Scene.Render(IRenderTarget)"/> derives for this output, and the
    /// number to divide by when going the other way. Not <c>ContentRect.Width / virtualWidth</c>:
    /// the rectangle's extents round outward to whole pixels and that ratio is therefore slightly
    /// wrong, by up to a pixel over the width of the frame.
    /// </remarks>
    public float RenderScale { get; }

    /// <summary>Gets the device-pixel rectangle the fitted frame occupies. Outside it is bar.</summary>
    public Rect ContentRect { get; }

    /// <summary>Gets whether any part of the output is bar rather than frame.</summary>
    public bool HasBars =>
        ContentRect.Width < OutputSize.Width || ContentRect.Height < OutputSize.Height;

    /// <summary>Resolves the mapping for one virtual resolution on one framebuffer.</summary>
    /// <param name="virtualResolution">The scene's finite, positive virtual resolution.</param>
    /// <param name="outputSize">The framebuffer's size in device pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The virtual resolution is not finite and positive, or the output size is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">The pair yields no finite, positive scale.</exception>
    public static Letterbox Resolve(in Vector2 virtualResolution, PixelSize outputSize)
    {
        var renderScale = FramePlacement.ResolveRenderScale(virtualResolution, outputSize);
        var contentRect = FramePlacement.ResolveContentRect(virtualResolution, outputSize, renderScale);
        return new Letterbox(outputSize, renderScale, contentRect);
    }

    /// <summary>Maps a device-pixel point on the framebuffer into the scene's virtual space.</summary>
    /// <param name="outputPoint">The point in device pixels, origin at the framebuffer's top-left.</param>
    /// <returns>The virtual-space point, unclamped — it may be negative or beyond the resolution.</returns>
    public Vector2 ToVirtual(Vector2 outputPoint) => (outputPoint - contentOrigin) / RenderScale;

    /// <summary>Maps a virtual-space point onto the framebuffer, in device pixels.</summary>
    /// <param name="virtualPoint">The point in the scene's virtual space.</param>
    /// <remarks>
    /// The exact inverse of <see cref="ToVirtual"/>, and the reason a test can assert a round trip
    /// rather than two hand-computed numbers.
    /// </remarks>
    public Vector2 ToOutput(Vector2 virtualPoint) => (virtualPoint * RenderScale) + contentOrigin;

    /// <summary>Writes the rectangles outside the content rect — the bars a host clears.</summary>
    /// <param name="destination">A span of at least two rectangles.</param>
    /// <returns>How many rectangles were written: zero, or two.</returns>
    /// <remarks>
    /// <para>
    /// Two or none, never one and never four. Fitting is uniform, so the frame is short on at most
    /// one axis; the two bars sit either side of the content rect on that axis and span the output
    /// on the other. The far bar is the wider one when the leftover is odd, which is
    /// <see cref="FramePlacement.ResolveContentOffset"/>'s rule and not a second decision.
    /// </para>
    /// <para>
    /// A zero-height or zero-width bar is not written, so a host clearing what this returns never
    /// issues an empty clear.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="destination"/> holds fewer than two.</exception>
    public int GetBars(Span<Rect> destination)
    {
        if (destination.Length < 2)
        {
            throw new ArgumentException(
                "Letterboxing produces up to two bars; the span must hold both.",
                nameof(destination));
        }

        var width = OutputSize.Width;
        var height = OutputSize.Height;
        if (ContentRect.Width < width)
        {
            var count = 0;
            if (ContentRect.Left > 0f)
            {
                destination[count++] = new Rect(0f, 0f, ContentRect.Left, height);
            }

            if (ContentRect.Right < width)
            {
                destination[count++] = new Rect(ContentRect.Right, 0f, width - ContentRect.Right, height);
            }

            return count;
        }

        if (ContentRect.Height < height)
        {
            var count = 0;
            if (ContentRect.Top > 0f)
            {
                destination[count++] = new Rect(0f, 0f, width, ContentRect.Top);
            }

            if (ContentRect.Bottom < height)
            {
                destination[count++] = new Rect(0f, ContentRect.Bottom, width, height - ContentRect.Bottom);
            }

            return count;
        }

        return 0;
    }
}
