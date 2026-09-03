using System.Numerics;

namespace Najm.Core.Tests.Input;

/// <summary>A node that declares the bounds a routing test needs and records what it is asked.</summary>
internal class HitNode(Rect hit) : Node2D
{
    /// <summary>Gets the visual bounds this node reports, defaulting to its hit bounds.</summary>
    internal Rect? Visual { get; init; }

    /// <summary>Gets or sets a name used in ordering assertions.</summary>
    internal string Name { get; init; } = string.Empty;

    /// <summary>Gets or sets whether <see cref="HitTest"/> answers true for points inside the bounds.</summary>
    internal bool Solid { get; set; } = true;

    /// <summary>Gets or sets a log every <see cref="HitTest"/> call appends its name to.</summary>
    internal List<string>? HitLog { get; set; }

    /// <inheritdoc />
    public override Rect HitBounds => hit;

    /// <inheritdoc />
    public override Rect VisualBounds => Visual ?? hit;

    /// <inheritdoc />
    public override bool HitTest(Vector2 local)
    {
        HitLog?.Add(Name);
        return Solid && base.HitTest(local);
    }

    /// <inheritdoc />
    public override string ToString() => Name.Length == 0 ? base.ToString()! : Name;
}
