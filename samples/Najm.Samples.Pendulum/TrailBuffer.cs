using System.Numerics;

namespace Najm.Samples.Pendulum;

/// <summary>A fixed-capacity ring buffer of recent points, oldest to newest.</summary>
/// <remarks>
/// <see cref="Snapshot"/> returns a view into a reused array, valid only until the next
/// <see cref="Push"/> — the same "synchronous, not retained" contract <see cref="PathBuilder"/>
/// uses for its own <c>Commands</c> span.
/// </remarks>
internal sealed class TrailBuffer
{
    private readonly Vector2[] ring;
    private readonly Vector2[] ordered;
    private int head;

    public TrailBuffer(int capacity)
    {
        ring = new Vector2[capacity];
        ordered = new Vector2[capacity];
    }

    public int Count { get; private set; }

    public void Push(Vector2 point)
    {
        var capacity = ring.Length;
        var writeIndex = (head + Count) % capacity;
        if (Count < capacity)
        {
            ring[writeIndex] = point;
            Count++;
        }
        else
        {
            ring[head] = point;
            head = (head + 1) % capacity;
        }
    }

    public ReadOnlySpan<Vector2> Snapshot()
    {
        var capacity = ring.Length;
        for (var i = 0; i < Count; i++)
        {
            ordered[i] = ring[(head + i) % capacity];
        }

        return ordered.AsSpan(0, Count);
    }
}
