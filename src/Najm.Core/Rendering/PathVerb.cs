namespace Najm.Core;

/// <summary>Identifies one command in a backend-neutral path.</summary>
public enum PathVerb
{
    /// <summary>Begins a new contour at one point.</summary>
    Move,

    /// <summary>Adds a straight segment to one point.</summary>
    Line,

    /// <summary>Adds a quadratic Bézier segment with a control point and endpoint.</summary>
    Quadratic,

    /// <summary>Adds a cubic Bézier segment with two control points and an endpoint.</summary>
    Cubic,

    /// <summary>Closes the current contour.</summary>
    Close,
}
