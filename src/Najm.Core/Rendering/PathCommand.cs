using System.Numerics;

namespace Najm.Core;

/// <summary>Contains one immutable command exposed by a <see cref="PathBuilder"/>.</summary>
public readonly struct PathCommand
{
    internal PathCommand(PathVerb verb, Vector2 point1, Vector2 point2, Vector2 point3)
    {
        Verb = verb;
        Point1 = point1;
        Point2 = point2;
        Point3 = point3;
    }

    /// <summary>Gets the command kind.</summary>
    public PathVerb Verb { get; }

    /// <summary>Gets the first point, whose meaning depends on <see cref="Verb"/>.</summary>
    public Vector2 Point1 { get; }

    /// <summary>Gets the second point, whose meaning depends on <see cref="Verb"/>.</summary>
    public Vector2 Point2 { get; }

    /// <summary>Gets the third point, whose meaning depends on <see cref="Verb"/>.</summary>
    public Vector2 Point3 { get; }
}
