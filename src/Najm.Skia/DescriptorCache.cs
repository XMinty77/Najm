namespace Najm.Skia;

/// <summary>
/// A bounded, value-keyed cache of the native objects Skia forces a context to allocate for a
/// portable descriptor, evicting least-recently-used entries and disposing what it evicts.
/// </summary>
/// <typeparam name="TKey">The portable descriptor value the native object is derived from.</typeparam>
/// <typeparam name="TValue">The native object, which this cache owns and disposes.</typeparam>
/// <remarks>
/// <para>
/// <strong>Why bounded.</strong> NAJM-SKIA II.2 asks that these caches "trim on pool epochs like
/// surfaces (I.5) so an abandoned gradient doesn't pin GPU memory forever." There is no surface pool
/// and no epoch in this tree yet, and inventing one to hang a trim off would be a larger, less
/// testable mechanism than the problem needs. A capacity with LRU eviction meets the same
/// requirement — an abandoned descriptor is released rather than retained for the context's life —
/// with a bound that holds under the case that motivates it: an animated brush or dash mints a
/// distinct descriptor value on every single frame, so an unbounded dictionary grows without limit
/// for as long as the animation runs. When an epoch mechanism does arrive it trims this cache; it
/// does not replace the bound, because a bound is what makes the growth rate of one frame
/// irrelevant.
/// </para>
/// <para>
/// <strong>Zero allocation in steady state.</strong> The entries are a fixed pool of nodes threaded
/// into an intrusive doubly-linked recency list, and the dictionary is sized to capacity at
/// construction. A hit is a dictionary lookup plus four reference writes to relink the node at the
/// head; a miss at capacity reuses the evicted node object rather than allocating a new one. So a
/// scene drawing a stable set of brushes allocates nothing at all here after its first frame, and a
/// scene animating one allocates only the native object it genuinely asked for.
/// </para>
/// <para>
/// <strong>The capacity's real constraint.</strong> LRU degrades badly only when the working set of
/// one frame exceeds the bound: every entry is then evicted before it is next needed and every draw
/// pays a construction. The capacity must therefore comfortably exceed the number of <em>distinct
/// descriptor values drawn in a single frame</em>, which is a palette-sized quantity even in scenes
/// with thousands of nodes — nodes share brushes; they do not each invent one. See
/// <see cref="SkiaDrawContext2D"/> for the number chosen and its arithmetic.
/// </para>
/// </remarks>
internal sealed class DescriptorCache<TKey, TValue>
    where TKey : notnull
    where TValue : IDisposable
{
    private readonly int capacity;
    private readonly Dictionary<TKey, Entry> entries;

    // Most recently used at the head. Both ends are needed: the head is where a hit moves to and
    // the tail is what an insertion at capacity evicts.
    private Entry? head;
    private Entry? tail;

    /// <summary>Creates an empty cache holding at most <paramref name="capacity"/> entries.</summary>
    /// <param name="capacity">The positive maximum number of live entries.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    internal DescriptorCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.capacity = capacity;

        // Sized to capacity so the dictionary never rehashes: entries are removed as often as they
        // are added once the cache is full, and a pre-sized table keeps both operations allocation
        // free.
        entries = new Dictionary<TKey, Entry>(capacity);
    }

    /// <summary>Gets how many descriptor values currently hold a cached native object.</summary>
    internal int Count => entries.Count;

    /// <summary>Gets the cache's documented maximum entry count.</summary>
    internal int Capacity => capacity;

    /// <summary>
    /// Gets how many entries this cache has evicted and disposed over its whole life.
    /// </summary>
    /// <remarks>
    /// A steady state over a stable set of descriptors must leave this unchanged; a rising count is
    /// how a test tells "the cache is bounded" apart from "the cache is thrashing".
    /// </remarks>
    internal int EvictionCount { get; private set; }

    /// <summary>
    /// Looks up the native object for a descriptor value, marking it most recently used on a hit.
    /// </summary>
    /// <param name="key">The portable descriptor value.</param>
    /// <param name="value">The cached native object, or the default when there is none.</param>
    /// <returns><see langword="true"/> when the value was cached.</returns>
    /// <remarks>
    /// This is a lookup rather than a get-or-create over a factory delegate, so that the caller
    /// constructs the native object with its own fields in hand and no delegate is created on the
    /// frame path.
    /// </remarks>
    internal bool TryGet(in TKey key, out TValue value)
    {
        if (entries.TryGetValue(key, out var found))
        {
            MoveToHead(found);
            value = found.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Caches a freshly constructed native object as the most recently used entry, evicting and
    /// disposing the least recently used one when the cache is already at capacity.
    /// </summary>
    /// <param name="key">The portable descriptor value, which must not already be cached.</param>
    /// <param name="value">The native object, whose ownership passes to the cache.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is already cached.</exception>
    internal void Add(in TKey key, TValue value)
    {
        Entry entry;
        if (entries.Count == capacity)
        {
            // Evict the tail and reuse its node, so a cache under churn allocates no bookkeeping at
            // all — only the native object the caller genuinely asked for.
            entry = tail!;
            entries.Remove(entry.Key);
            Unlink(entry);
            entry.Value.Dispose();
            EvictionCount++;
            entry.Key = key;
            entry.Value = value;
        }
        else
        {
            entry = new Entry(key, value);
        }

        LinkAtHead(entry);
        entries.Add(key, entry);
    }

    /// <summary>Disposes every cached native object and empties the cache.</summary>
    internal void Clear()
    {
        for (var entry = head; entry is not null; entry = entry.Next)
        {
            entry.Value.Dispose();
        }

        entries.Clear();
        head = null;
        tail = null;
    }

    private void MoveToHead(Entry entry)
    {
        if (ReferenceEquals(entry, head))
        {
            return;
        }

        Unlink(entry);
        LinkAtHead(entry);
    }

    private void LinkAtHead(Entry entry)
    {
        entry.Previous = null;
        entry.Next = head;
        if (head is not null)
        {
            head.Previous = entry;
        }

        head = entry;
        tail ??= entry;
    }

    private void Unlink(Entry entry)
    {
        if (entry.Previous is not null)
        {
            entry.Previous.Next = entry.Next;
        }
        else if (ReferenceEquals(head, entry))
        {
            head = entry.Next;
        }

        if (entry.Next is not null)
        {
            entry.Next.Previous = entry.Previous;
        }
        else if (ReferenceEquals(tail, entry))
        {
            tail = entry.Previous;
        }

        entry.Previous = null;
        entry.Next = null;
    }

    /// <summary>One cached descriptor, and its place in the recency list.</summary>
    private sealed class Entry(TKey key, TValue value)
    {
        internal TKey Key { get; set; } = key;

        internal TValue Value { get; set; } = value;

        internal Entry? Previous { get; set; }

        internal Entry? Next { get; set; }
    }
}
