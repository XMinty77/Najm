namespace Najm.Core;

/// <summary>Contains input delivered for one tick.</summary>
/// <remarks>
/// Input routing has not landed yet, so this M0 shape represents only the
/// canonical empty block required by deterministic clocks. Unlike other timing
/// structs, <c>default(InputBlock)</c> is valid and exactly equals
/// <see cref="Empty"/>. The type will retain that property when pooled event and
/// snapshot views are added.
/// </remarks>
public readonly struct InputBlock
{
    /// <summary>Gets the canonical allocation-free empty input block.</summary>
    public static InputBlock Empty => default;

    /// <summary>Gets whether this block has no events or active snapshots.</summary>
    public bool IsEmpty => true;
}

