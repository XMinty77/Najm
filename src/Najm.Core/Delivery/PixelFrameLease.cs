namespace Najm.Core;

/// <summary>
/// Owns one frame's worth of pooled pixel memory for the span between capture and delivery.
/// </summary>
/// <remarks>
/// <para>
/// A lease carries the frame's dimensions, its row stride, its <see cref="PixelFormat"/>, and the
/// pooled buffer those describe. It exists so a capture never hands an <see cref="IImage"/> or a
/// live surface to a sink: pixels are copied once into a lease, and the lease — not the surface —
/// is what the sink owns while it encodes.
/// </para>
/// <para>
/// <strong>Ownership.</strong> The producer rents a lease, fills it, and passes it to
/// <see cref="IFrameSink.Submit(long, PixelFrameLease)"/>. Ownership transfers on entry to that
/// call: the sink disposes the lease, after synchronous use or after an asynchronous encoder has
/// consumed it, and the producer must not touch it again. Disposing a lease twice throws, because
/// the second disposal would release a buffer the pool may already have handed to someone else.
/// </para>
/// <para>
/// <strong>Pooling, and why not <c>ArrayPool&lt;byte&gt;.Shared</c>.</strong> Both the buffer and
/// the lease object itself are recycled, so a warm offline loop allocates nothing per frame — the
/// steady state of a long render is one buffer handed back and forth. The shared array pool is not
/// used because it caps pooled arrays at one mebibyte: every frame above 512×512 would miss the
/// pool and allocate, and a 4K frame (about 33 MiB) would allocate and abandon a large-object-heap
/// array sixty times a second. An offline run instead renders one constant frame size for its whole
/// duration, which an exact-fit free list serves perfectly. The free list is per-thread and holds at
/// most <see cref="MaxPooledLeases"/> idle leases; <see cref="TrimPool"/> releases them.
/// </para>
/// <para>
/// The buffer's contents are undefined until the producer writes them. A lease is single-threaded,
/// like the rest of the engine, apart from being disposable on whichever thread finally consumes it.
/// </para>
/// </remarks>
public sealed class PixelFrameLease : IDisposable
{
    /// <summary>The number of idle leases one thread retains for reuse.</summary>
    /// <remarks>
    /// A synchronous offline loop keeps exactly one lease alive at a time, so a small cap is enough
    /// to make the steady state allocation-free while bounding retained pixel memory to a handful of
    /// frames for a bounded asynchronous queue.
    /// </remarks>
    private const int MaxPooledLeases = 4;

    [ThreadStatic]
    private static PixelFrameLease? freeList;

    [ThreadStatic]
    private static int freeCount;

    private PixelFrameLease? next;
    private byte[] buffer = [];
    private int width;
    private int height;
    private int stride;
    private int byteLength;
    private PixelFormat format;
    private bool rented;

    private PixelFrameLease()
    {
    }

    /// <summary>Gets the frame width in pixels.</summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public int Width
    {
        get
        {
            EnsureRented();
            return width;
        }
    }

    /// <summary>Gets the frame height in pixels.</summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public int Height
    {
        get
        {
            EnsureRented();
            return height;
        }
    }

    /// <summary>Gets the byte distance between the starts of two consecutive rows.</summary>
    /// <remarks>
    /// This is at least <see cref="RowBytes"/> and may exceed it when the producer asked for padded
    /// rows. Bytes between <see cref="RowBytes"/> and the stride are not part of the image.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public int Stride
    {
        get
        {
            EnsureRented();
            return stride;
        }
    }

    /// <summary>Gets the meaningful bytes in one row, which is always four bytes per pixel.</summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public int RowBytes
    {
        get
        {
            EnsureRented();
            return width * 4;
        }
    }

    /// <summary>Gets the byte and alpha layout of the pixels this lease carries.</summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public PixelFormat Format
    {
        get
        {
            EnsureRented();
            return format;
        }
    }

    /// <summary>Gets the total addressable byte length, which is <see cref="Stride"/> × <see cref="Height"/>.</summary>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public int ByteLength
    {
        get
        {
            EnsureRented();
            return byteLength;
        }
    }

    /// <summary>Gets the leased pixel memory, exactly <see cref="ByteLength"/> bytes long.</summary>
    /// <remarks>
    /// The span is a view onto pooled memory and is valid only until the lease is disposed. Never
    /// store it beyond the call that consumes the frame.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public Span<byte> Pixels
    {
        get
        {
            EnsureRented();
            return buffer.AsSpan(0, byteLength);
        }
    }

    /// <summary>Gets the meaningful bytes of one row, excluding any stride padding.</summary>
    /// <param name="y">The zero-based, top-origin row index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is outside the frame.</exception>
    /// <exception cref="ObjectDisposedException">The lease has been disposed.</exception>
    public Span<byte> Row(int y)
    {
        EnsureRented();
        if ((uint)y >= (uint)height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Row indices must lie in [0, {height}).");
        }

        return buffer.AsSpan(y * stride, width * 4);
    }

    /// <summary>Rents a lease for a tightly packed frame, whose stride is four bytes per pixel.</summary>
    /// <param name="width">The positive frame width in pixels.</param>
    /// <param name="height">The positive frame height in pixels.</param>
    /// <param name="format">The byte and alpha layout the producer will write.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="format"/> is not a defined format.</exception>
    public static PixelFrameLease Rent(int width, int height, PixelFormat format) =>
        Rent(width, height, checked(width * 4), format);

    /// <summary>Rents a lease for a frame with an explicit row stride.</summary>
    /// <param name="width">The positive frame width in pixels.</param>
    /// <param name="height">The positive frame height in pixels.</param>
    /// <param name="stride">The row stride in bytes, at least four bytes per pixel.</param>
    /// <param name="format">The byte and alpha layout the producer will write.</param>
    /// <remarks>
    /// The returned lease is recycled from this thread's free list when one is idle and large
    /// enough, so a loop that rents and disposes one frame size repeatedly allocates only once.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, or <paramref name="stride"/> cannot hold one row.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="format"/> is not a defined format.</exception>
    public static PixelFrameLease Rent(int width, int height, int stride, PixelFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                $"A {width}-pixel row needs at least {rowBytes} stride bytes.");
        }
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentException("The requested pixel format is not defined.", nameof(format));
        }

        var requiredBytes = checked(stride * height);
        var lease = freeList;
        if (lease is null)
        {
            lease = new PixelFrameLease();
        }
        else
        {
            freeList = lease.next;
            lease.next = null;
            freeCount--;
        }

        if (lease.buffer.Length < requiredBytes)
        {
            lease.buffer = GC.AllocateUninitializedArray<byte>(requiredBytes);
        }

        lease.width = width;
        lease.height = height;
        lease.stride = stride;
        lease.byteLength = requiredBytes;
        lease.format = format;
        lease.rented = true;
        return lease;
    }

    /// <summary>Releases every idle lease this thread retains, freeing their pixel memory.</summary>
    /// <remarks>
    /// Call this after a render whose frame size will not recur — a 4K run leaves multi-mebibyte
    /// buffers on the free list, and nothing else ever shrinks them. Leases currently rented are
    /// unaffected.
    /// </remarks>
    public static void TrimPool()
    {
        for (var lease = freeList; lease is not null;)
        {
            var following = lease.next;
            lease.next = null;
            lease.buffer = [];
            lease = following;
        }

        freeList = null;
        freeCount = 0;
    }

    /// <summary>Returns this lease's memory to the pool.</summary>
    /// <remarks>
    /// The owner — the sink, once <see cref="IFrameSink.Submit(long, PixelFrameLease)"/> has been
    /// entered — disposes exactly once. This is deliberately not idempotent: a second disposal means
    /// two owners believe they hold the frame, and silently tolerating it would eventually hand one
    /// buffer to two renters.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The lease has already been disposed.</exception>
    public void Dispose()
    {
        EnsureRented();
        rented = false;

        if (freeCount < MaxPooledLeases)
        {
            next = freeList;
            freeList = this;
            freeCount++;
            return;
        }

        // Not worth retaining: drop the buffer so it can be collected even if the caller keeps a
        // reference to this now-inert lease.
        buffer = [];
    }

    private void EnsureRented()
    {
        if (!rented)
        {
            throw new ObjectDisposedException(
                nameof(PixelFrameLease),
                "This pixel frame lease has been disposed and its memory returned to the pool.");
        }
    }
}
