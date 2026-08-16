namespace Najm.Core;

/// <summary>Specifies the geometry a stroke adds where two segments meet.</summary>
public enum LineJoin
{
    /// <summary>
    /// Extends the outer edges to a point, falling back to a bevel past the paint's miter limit.
    /// This is the default.
    /// </summary>
    Miter,

    /// <summary>Rounds the outer corner with an arc of half the stroke width.</summary>
    Round,

    /// <summary>Cuts the outer corner with a straight edge.</summary>
    Bevel,
}
