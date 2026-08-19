namespace Najm.Core;

/// <summary>Selects how an arc's two ends are joined into a contour.</summary>
/// <remarks>
/// The mode is geometry, not paint: it decides which commands the arc appends, so a filled
/// <see cref="Open"/> arc still shows the implicit closing chord every fill rule draws. Choose
/// <see cref="Open"/> for a stroked arc, <see cref="Chord"/> for a circular segment, and
/// <see cref="Pie"/> for a wedge.
/// </remarks>
public enum ArcMode
{
    /// <summary>Leaves the contour open, running from the start point to the end point.</summary>
    Open,

    /// <summary>Closes the contour directly from the end point back to the start point.</summary>
    Chord,

    /// <summary>Runs from the center out to the start point and closes back through the center.</summary>
    Pie,
}
