namespace Najm.Core;

/// <summary>Specifies the geometry a stroke adds at an open contour's ends.</summary>
public enum LineCap
{
    /// <summary>Ends the stroke exactly at the endpoint. This is the default.</summary>
    Butt,

    /// <summary>Ends the stroke with a semicircle of half the stroke width.</summary>
    Round,

    /// <summary>Ends the stroke with a square extending half the stroke width beyond the endpoint.</summary>
    Square,
}
