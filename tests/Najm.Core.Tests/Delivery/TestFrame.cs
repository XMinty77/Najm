namespace Najm.Core.Tests.Delivery;

/// <summary>Builds small frames with known contents, for the diagnostics tests.</summary>
/// <remarks>
/// Every helper takes and returns <em>logical</em> RGBA and places the bytes where the requested
/// <see cref="PixelFormat"/> says they go. That is deliberate: a test that hand-wrote byte offsets
/// would agree with a channel-mapping bug in the code under test rather than catching it.
/// </remarks>
internal static class TestFrame
{
    /// <summary>Creates a frame in which every pixel is the same colour.</summary>
    internal static PixelFrameLease Uniform(
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha = 255,
        PixelFormat format = PixelFormat.Rgba8888)
    {
        var lease = PixelFrameLease.Rent(width, height, format);
        lease.Pixels.Clear();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Set(lease, x, y, red, green, blue, alpha);
            }
        }

        return lease;
    }

    /// <summary>Creates a frame from an explicit list of pixels in reading order.</summary>
    internal static PixelFrameLease FromPixels(
        int width,
        int height,
        PixelFormat format,
        params (byte Red, byte Green, byte Blue, byte Alpha)[] pixels)
    {
        Assert.HasCount(width * height, pixels, "A frame needs exactly one colour per pixel.");
        var lease = PixelFrameLease.Rent(width, height, format);
        lease.Pixels.Clear();
        for (var index = 0; index < pixels.Length; index++)
        {
            var (red, green, blue, alpha) = pixels[index];
            Set(lease, index % width, index / width, red, green, blue, alpha);
        }

        return lease;
    }

    /// <summary>Creates a one-row frame of opaque neutral greys, one pixel per level given.</summary>
    /// <remarks>
    /// A neutral grey has the useful property that its Rec. 709 luma is its own level — the weights
    /// sum to one — so a grey ramp lets a luma expectation be written down without arithmetic.
    /// </remarks>
    internal static PixelFrameLease Greys(params byte[] levels)
    {
        var lease = PixelFrameLease.Rent(levels.Length, 1, PixelFormat.Rgba8888);
        lease.Pixels.Clear();
        for (var x = 0; x < levels.Length; x++)
        {
            Set(lease, x, 0, levels[x], levels[x], levels[x], 255);
        }

        return lease;
    }

    /// <summary>Writes one logical RGBA pixel into a frame, honouring the frame's byte order.</summary>
    internal static void Set(
        PixelFrameLease lease,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha = 255)
    {
        var row = lease.Row(y);
        var offset = x * 4;
        var (redOffset, blueOffset) = lease.Format == PixelFormat.Bgra8888Premul ? (2, 0) : (0, 2);
        row[offset + redOffset] = red;
        row[offset + 1] = green;
        row[offset + blueOffset] = blue;
        row[offset + 3] = alpha;
    }
}
