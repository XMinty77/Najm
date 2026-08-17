namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The allocation-critical type: a lease has to describe a frame exactly and cost nothing after the
/// first one.
/// </summary>
[TestClass]
public sealed class PixelFrameLeaseTests
{
    [TestMethod]
    public void ALeaseDescribesTheFrameItWasRentedFor()
    {
        using var lease = PixelFrameLease.Rent(7, 3, PixelFormat.Rgba8888Premul);

        Assert.AreEqual(7, lease.Width);
        Assert.AreEqual(3, lease.Height);
        Assert.AreEqual(28, lease.Stride, "A tightly packed row is four bytes per pixel.");
        Assert.AreEqual(28, lease.RowBytes);
        Assert.AreEqual(84, lease.ByteLength);
        Assert.AreEqual(84, lease.Pixels.Length, "The span is the frame, not the pooled array.");
        Assert.AreEqual(PixelFormat.Rgba8888Premul, lease.Format);
    }

    [TestMethod]
    public void APaddedStrideKeepsRowsAddressableWithoutTheirPadding()
    {
        using var lease = PixelFrameLease.Rent(4, 2, stride: 24, PixelFormat.Rgba8888);

        Assert.AreEqual(24, lease.Stride);
        Assert.AreEqual(16, lease.RowBytes, "Padding is not part of the image.");
        Assert.AreEqual(48, lease.ByteLength);
        Assert.AreEqual(16, lease.Row(0).Length);
        Assert.AreEqual(16, lease.Row(1).Length);

        lease.Pixels.Clear();
        lease.Row(1).Fill(0xAB);

        // Row 1 starts one stride in, so the padding after row 0 must be untouched.
        Assert.AreEqual(0, lease.Pixels[16]);
        Assert.AreEqual(0xAB, lease.Pixels[24]);
        Assert.AreEqual(0xAB, lease.Pixels[39]);
        Assert.AreEqual(0, lease.Pixels[40], "Row 1's padding is past its meaningful bytes.");
    }

    [TestMethod]
    public void RentingRejectsShapesThatCannotDescribeAFrame()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelFrameLease.Rent(0, 4, PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelFrameLease.Rent(4, -1, PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelFrameLease.Rent(4, 4, stride: 15, PixelFormat.Rgba8888));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PixelFrameLease.Rent(4, 4, (PixelFormat)99));
    }

    [TestMethod]
    public void ADisposedLeaseIsInertAndCannotBeDisposedTwice()
    {
        var lease = PixelFrameLease.Rent(2, 2, PixelFormat.Rgba8888);
        lease.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = lease.Width);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = lease.Pixels.Length);
        Assert.ThrowsExactly<ObjectDisposedException>(lease.Dispose);
    }

    [TestMethod]
    public void RowIndicesOutsideTheFrameAreRejected()
    {
        using var lease = PixelFrameLease.Rent(2, 2, PixelFormat.Rgba8888);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = lease.Row(-1).Length);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = lease.Row(2).Length);
    }

    [TestMethod]
    public void RentAndReleaseOfOneFrameSizeAllocatesOnlyOnce()
    {
        // This is the property the offline loop depends on: a long render rents one frame shape over
        // and over, and after the first the pool hands the same buffer and the same lease object
        // back. ArrayPool<byte>.Shared would not do this — it caps pooled arrays at one mebibyte, so
        // every frame above 512×512 would allocate.
        const int Width = 640;
        const int Height = 360;

        PixelFrameLease.TrimPool();
        var cycle = 0;

        var reading = AllocationProbe.AssertNoneAllocated(
            5_000,
            () =>
            {
                var lease = PixelFrameLease.Rent(Width, Height, PixelFormat.Rgba8888);
                lease.Pixels[0] = (byte)cycle;
                cycle++;
                lease.Dispose();
            },
            $"Rent/release cycles of a {Width}×{Height} frame");

        Assert.AreEqual(reading.Invocations, cycle);
    }

    [TestMethod]
    public void TrimmingThePoolReleasesIdleBuffersWithoutBreakingLaterRents()
    {
        PixelFrameLease.Rent(16, 16, PixelFormat.Rgba8888).Dispose();
        PixelFrameLease.TrimPool();

        using var lease = PixelFrameLease.Rent(16, 16, PixelFormat.Rgba8888);

        Assert.AreEqual(1024, lease.ByteLength);
    }
}
