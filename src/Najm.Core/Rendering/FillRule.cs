namespace Najm.Core;

/// <summary>Specifies how a path determines whether an enclosed point is filled.</summary>
public enum FillRule
{
    /// <summary>Fill points whose signed winding count is nonzero.</summary>
    NonZero,

    /// <summary>Fill points crossed by an odd number of contour edges.</summary>
    EvenOdd,
}
