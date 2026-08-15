using System.Numerics;

namespace Najm.Core;

/// <summary>
/// Builds reusable backend-neutral path geometry without allocating again at a stable capacity.
/// </summary>
/// <remarks>
/// Backends consume <see cref="Commands"/> synchronously. Mutating or resetting the builder
/// invalidates previously obtained spans. Persistent backend-cached geometry uses a separate baked
/// path handle; a draw context never retains an arbitrary mutable builder.
/// </remarks>
public sealed class PathBuilder
{
    private PathCommand[] commands;
    private FillRule fillRule;
    private bool contourOpen;

    /// <summary>Creates an empty path builder.</summary>
    /// <param name="fillRule">The path's initial fill rule.</param>
    /// <param name="initialCapacity">The optional non-negative command capacity to reserve.</param>
    public PathBuilder(FillRule fillRule = FillRule.NonZero, int initialCapacity = 0)
    {
        if (!Enum.IsDefined(fillRule))
        {
            throw new ArgumentException("The fill rule is not defined.", nameof(fillRule));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        this.fillRule = fillRule;
        commands = initialCapacity == 0 ? [] : new PathCommand[initialCapacity];
    }

    /// <summary>Gets or sets the rule used when this path is filled.</summary>
    public FillRule FillRule
    {
        get => fillRule;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentException("The fill rule is not defined.", nameof(value));
            }
            if (fillRule == value)
            {
                return;
            }

            fillRule = value;
        }
    }

    /// <summary>Gets the number of commands currently in the path.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the current commands. The span is valid only until the builder is next mutated.
    /// </summary>
    public ReadOnlySpan<PathCommand> Commands => commands.AsSpan(0, Count);

    /// <summary>Removes all commands while retaining allocated capacity and the current fill rule.</summary>
    public void Reset()
    {
        Count = 0;
        contourOpen = false;
    }

    /// <summary>Begins a new contour.</summary>
    public PathBuilder MoveTo(float x, float y)
    {
        var point = CreateFinitePoint(x, y);
        Append(new PathCommand(PathVerb.Move, point, default, default));
        contourOpen = true;
        return this;
    }

    /// <summary>Adds a straight segment to the current contour.</summary>
    public PathBuilder LineTo(float x, float y)
    {
        EnsureContour();
        var point = CreateFinitePoint(x, y);
        Append(new PathCommand(PathVerb.Line, point, default, default));
        return this;
    }

    /// <summary>Adds a quadratic Bézier segment to the current contour.</summary>
    public PathBuilder QuadTo(float controlX, float controlY, float endX, float endY)
    {
        EnsureContour();
        var control = CreateFinitePoint(controlX, controlY);
        var end = CreateFinitePoint(endX, endY);
        Append(new PathCommand(PathVerb.Quadratic, control, end, default));
        return this;
    }

    /// <summary>Adds a cubic Bézier segment to the current contour.</summary>
    public PathBuilder CubicTo(
        float control1X,
        float control1Y,
        float control2X,
        float control2Y,
        float endX,
        float endY)
    {
        EnsureContour();
        var control1 = CreateFinitePoint(control1X, control1Y);
        var control2 = CreateFinitePoint(control2X, control2Y);
        var end = CreateFinitePoint(endX, endY);
        Append(new PathCommand(PathVerb.Cubic, control1, control2, end));
        return this;
    }

    /// <summary>Closes the current contour.</summary>
    public PathBuilder Close()
    {
        EnsureContour();
        Append(new PathCommand(PathVerb.Close, default, default, default));
        contourOpen = false;
        return this;
    }

    private static Vector2 CreateFinitePoint(float x, float y)
    {
        if (!float.IsFinite(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Path coordinates must be finite.");
        }
        if (!float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Path coordinates must be finite.");
        }
        return new Vector2(x, y);
    }

    private void EnsureContour()
    {
        if (!contourOpen)
        {
            throw new InvalidOperationException("Begin a contour with MoveTo before adding or closing segments.");
        }
    }

    private void Append(PathCommand command)
    {
        if (Count == commands.Length)
        {
            var newCapacity = commands.Length == 0 ? 16 : checked(commands.Length * 2);
            Array.Resize(ref commands, newCapacity);
        }

        commands[Count++] = command;
    }
}
