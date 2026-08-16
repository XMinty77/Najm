namespace Najm.Core;

/// <summary>Specifies how a brush paints outside the extent its geometry defines.</summary>
public enum SpreadMode
{
    /// <summary>Extends the first and last stop, or the pattern edge, outward. This is the default.</summary>
    Clamp,

    /// <summary>Repeats the brush extent in the same direction.</summary>
    Repeat,

    /// <summary>Repeats the brush extent, reflecting every other repetition.</summary>
    Mirror,
}
