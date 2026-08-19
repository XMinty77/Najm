using System.Numerics;
using System.Runtime.CompilerServices;
using Najm.Utils;

namespace Najm.Core;

/// <summary>A backend-neutral value describing what fills or strokes geometry.</summary>
/// <remarks>
/// <para>
/// <c>default(Brush)</c> is a transparent solid brush and is exactly equal to
/// <c>Brush.Solid(Color.Transparent)</c>. All coordinates — gradient endpoints, centers, and radii —
/// are local units, like every other size in the engine.
/// </para>
/// <para>
/// <b>Stop storage.</b> A gradient's stops are a variable-length payload, so the struct holds a
/// reference to one array that the factory copies from the caller's span and never hands out again:
/// the brush stays a copyable value while the stops are a shared, immutable payload, and passing a
/// brush around allocates nothing. Only the factories allocate, once, when the brush value is built.
/// </para>
/// <para>
/// <b>Interpolation is straight, not premultiplied.</b> Between two stops a backend interpolates R,
/// G, B, and A independently on the straight (unpremultiplied) values this descriptor stores. That
/// is the model SVG, CSS, Skia, and PDF all specify, and it is not negotiable per brush. Its one
/// sharp edge is fading out: a stop at <see cref="Najm.Utils.Color.Transparent"/> is transparent
/// <em>black</em>, so a ramp toward it walks RGB to zero as alpha falls and smears grey through the
/// fade. Fade to the same color at zero alpha instead — <see cref="Najm.Utils.Color.Fade"/> — or let
/// <see cref="LinearFade"/> and <see cref="RadialFade"/> build the ramp so the question never comes
/// up.
/// </para>
/// <para>
/// <b>Equality.</b> Two brushes are equal when their descriptors match and their stop
/// <em>contents</em> match — never by array reference — because backends key their gradient shader
/// caches by brush value (NAJM-SKIA II.2). Reference equality would miss on every independently
/// constructed copy of the same ramp and defeat the cache. <see cref="GetHashCode"/> mixes the stop
/// contents for the same reason, and is computed on demand rather than cached in a field so that
/// <c>default(Brush)</c>, which carries no array, hashes identically to an explicitly constructed
/// transparent solid brush. Stop counts are small, so both operations stay cheap and
/// allocation-free. Image patterns compare their <see cref="IImage"/> handle by reference, which is
/// that handle's identity.
/// </para>
/// </remarks>
public readonly struct Brush : IEquatable<Brush>
{
    private readonly Vector2 origin;
    private readonly Vector2 endPoint;
    private readonly float radius;
    private readonly IImage? image;
    private readonly GradientStop[]? stops;

    private Brush(
        BrushKind kind,
        Color color,
        Vector2 origin,
        Vector2 endPoint,
        float radius,
        SpreadMode spread,
        IImage? image,
        GradientStop[]? stops)
    {
        Kind = kind;
        Color = color;
        Spread = spread;
        this.origin = origin;
        this.endPoint = endPoint;
        this.radius = radius;
        this.image = image;
        this.stops = stops;
    }

    /// <summary>Gets which member of the portable brush subset this value describes.</summary>
    public BrushKind Kind { get; }

    /// <summary>
    /// Gets the flat sRGB-referenced color of a <see cref="BrushKind.Solid"/> brush. It is
    /// transparent for every other kind, whose color comes from the stops or the image.
    /// </summary>
    public Color Color { get; }

    /// <summary>Gets how the brush paints outside its own extent.</summary>
    public SpreadMode Spread { get; }

    /// <summary>Gets a linear gradient's start point in local units. Other kinds report zero.</summary>
    public Vector2 Start => Kind == BrushKind.LinearGradient ? origin : Vector2.Zero;

    /// <summary>Gets a linear gradient's end point in local units. Other kinds report zero.</summary>
    public Vector2 End => Kind == BrushKind.LinearGradient ? endPoint : Vector2.Zero;

    /// <summary>Gets a radial gradient's center in local units. Other kinds report zero.</summary>
    public Vector2 Center => Kind == BrushKind.RadialGradient ? origin : Vector2.Zero;

    /// <summary>Gets a radial gradient's positive radius in local units. Other kinds report zero.</summary>
    public float Radius => radius;

    /// <summary>Gets the image tiled by an <see cref="BrushKind.ImagePattern"/> brush, or null.</summary>
    public IImage? Image => image;

    /// <summary>
    /// Gets the gradient's stops in non-decreasing offset order. The span is empty for kinds that
    /// carry none, and it views a payload no caller can mutate.
    /// </summary>
    public ReadOnlySpan<GradientStop> Stops => stops;

    /// <summary>Creates a brush painting one flat color.</summary>
    /// <param name="color">The sRGB-referenced color.</param>
    public static Brush Solid(Color color) =>
        new(BrushKind.Solid, color, Vector2.Zero, Vector2.Zero, 0f, SpreadMode.Clamp, image: null, stops: null);

    /// <summary>Creates a brush interpolating stops along a local-unit segment.</summary>
    /// <param name="start">The finite local-unit point reached by offset zero.</param>
    /// <param name="end">The finite local-unit point reached by offset one.</param>
    /// <param name="stops">At least two stops in non-decreasing offset order; copied.</param>
    /// <param name="spread">How the gradient paints beyond the segment.</param>
    /// <remarks>
    /// Stops interpolate straight, unpremultiplied color. To fade out, end on the ramp's own color at
    /// zero alpha (<see cref="Najm.Utils.Color.Fade"/>), not on
    /// <see cref="Najm.Utils.Color.Transparent"/>, which is transparent <em>black</em> and drags the
    /// midtones toward grey. <see cref="LinearFade"/> is the two-stop form of that.
    /// </remarks>
    public static Brush Linear(
        Vector2 start,
        Vector2 end,
        ReadOnlySpan<GradientStop> stops,
        SpreadMode spread = SpreadMode.Clamp)
    {
        RequireFinitePoint(start, nameof(start));
        RequireFinitePoint(end, nameof(end));
        RequireSpread(spread);

        return new Brush(
            BrushKind.LinearGradient,
            Color.Transparent,
            start,
            end,
            0f,
            spread,
            image: null,
            CopyStops(stops));
    }

    /// <summary>Creates a brush interpolating stops outward from a local-unit center.</summary>
    /// <param name="center">The finite local-unit center reached by offset zero.</param>
    /// <param name="radius">The finite positive local-unit radius reached by offset one.</param>
    /// <param name="stops">At least two stops in non-decreasing offset order; copied.</param>
    /// <param name="spread">How the gradient paints beyond the radius.</param>
    /// <remarks>
    /// Stops interpolate straight, unpremultiplied color. To fade out, end on the ramp's own color at
    /// zero alpha (<see cref="Najm.Utils.Color.Fade"/>), not on
    /// <see cref="Najm.Utils.Color.Transparent"/>, which is transparent <em>black</em> and drags the
    /// midtones toward grey — the classic dirty halo. <see cref="RadialFade"/> is the two-stop form of
    /// that, and it is what a soft glow wants.
    /// </remarks>
    public static Brush Radial(
        Vector2 center,
        float radius,
        ReadOnlySpan<GradientStop> stops,
        SpreadMode spread = SpreadMode.Clamp)
    {
        RequireFinitePoint(center, nameof(center));
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "A radial gradient's radius must be finite and positive.");
        }
        RequireSpread(spread);

        return new Brush(
            BrushKind.RadialGradient,
            Color.Transparent,
            center,
            Vector2.Zero,
            radius,
            spread,
            image: null,
            CopyStops(stops));
    }

    /// <summary>Creates a linear gradient from one color to that same color at zero alpha.</summary>
    /// <param name="start">The finite local-unit point where the color is at full strength.</param>
    /// <param name="end">The finite local-unit point where it has faded to nothing.</param>
    /// <param name="color">The color to fade out. Its own alpha is the ramp's starting alpha.</param>
    /// <param name="spread">How the gradient paints beyond the segment.</param>
    /// <returns>A two-stop gradient from <paramref name="color"/> to <c>color.Fade()</c>.</returns>
    /// <remarks>
    /// The point of this factory is that it is impossible to get the trap wrong with it: it builds
    /// the ramp whose RGB never moves, so only coverage falls off. Reach for
    /// <see cref="Linear(Vector2, Vector2, ReadOnlySpan{GradientStop}, SpreadMode)"/> directly when
    /// the falloff needs a shaped curve rather than a straight line — and then end that stop list on
    /// <see cref="Najm.Utils.Color.Fade"/> for the same reason.
    /// </remarks>
    public static Brush LinearFade(
        Vector2 start,
        Vector2 end,
        Color color,
        SpreadMode spread = SpreadMode.Clamp) =>
        Linear(start, end, [new GradientStop(0f, color), new GradientStop(1f, color.Fade())], spread);

    /// <summary>Creates a radial gradient from one color at the center to nothing at the rim.</summary>
    /// <param name="center">The finite local-unit center, where the color is at full strength.</param>
    /// <param name="radius">The finite positive local-unit radius, where it has faded to nothing.</param>
    /// <param name="color">The color to fade out. Its own alpha is the ramp's peak alpha.</param>
    /// <param name="spread">How the gradient paints beyond the radius.</param>
    /// <returns>A two-stop gradient from <paramref name="color"/> to <c>color.Fade()</c>.</returns>
    /// <remarks>
    /// This is the correct soft glow in one call: RGB is constant across the whole disc and only
    /// alpha ramps, so the halo stays the color of the light instead of bruising grey halfway out.
    /// A shaped falloff — light does not fall off linearly — still belongs in an explicit stop list
    /// through <see cref="Radial(Vector2, float, ReadOnlySpan{GradientStop}, SpreadMode)"/>; the rule
    /// about the far end is the same there.
    /// </remarks>
    public static Brush RadialFade(
        Vector2 center,
        float radius,
        Color color,
        SpreadMode spread = SpreadMode.Clamp) =>
        Radial(center, radius, [new GradientStop(0f, color), new GradientStop(1f, color.Fade())], spread);

    /// <summary>Creates a brush tiling an image handle across the painted geometry.</summary>
    /// <param name="image">The image handle to tile. The brush does not take ownership.</param>
    /// <param name="spread">How the pattern repeats beyond the image bounds.</param>
    public static Brush Pattern(IImage image, SpreadMode spread = SpreadMode.Clamp)
    {
        ArgumentNullException.ThrowIfNull(image);
        RequireSpread(spread);

        return new Brush(
            BrushKind.ImagePattern,
            Color.Transparent,
            Vector2.Zero,
            Vector2.Zero,
            0f,
            spread,
            image,
            stops: null);
    }

    /// <inheritdoc />
    public bool Equals(Brush other) =>
        Kind == other.Kind &&
        Spread == other.Spread &&
        Color.Equals(other.Color) &&
        origin.Equals(other.origin) &&
        endPoint.Equals(other.endPoint) &&
        radius.Equals(other.radius) &&
        ReferenceEquals(image, other.image) &&
        Stops.SequenceEqual(other.Stops);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Brush other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Kind);
        hash.Add(Spread);
        hash.Add(Color);
        hash.Add(origin);
        hash.Add(endPoint);
        hash.Add(radius);
        hash.Add(image is null ? 0 : RuntimeHelpers.GetHashCode(image));
        foreach (var stop in Stops)
        {
            hash.Add(stop);
        }

        return hash.ToHashCode();
    }

    /// <summary>Tests two brushes for value equality, comparing gradient stop contents.</summary>
    public static bool operator ==(Brush left, Brush right) => left.Equals(right);

    /// <summary>Tests two brushes for value inequality, comparing gradient stop contents.</summary>
    public static bool operator !=(Brush left, Brush right) => !left.Equals(right);

    private static GradientStop[] CopyStops(ReadOnlySpan<GradientStop> stops)
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("A gradient requires at least two stops.", nameof(stops));
        }

        for (var index = 1; index < stops.Length; index++)
        {
            if (stops[index].Offset < stops[index - 1].Offset)
            {
                throw new ArgumentException(
                    "Gradient stop offsets must be in non-decreasing order.",
                    nameof(stops));
            }
        }

        return stops.ToArray();
    }

    private static void RequireFinitePoint(Vector2 point, string parameterName)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Brush coordinates must be finite.");
        }
    }

    private static void RequireSpread(SpreadMode spread)
    {
        if (!Enum.IsDefined(spread))
        {
            throw new ArgumentException("The spread mode is not defined.", nameof(spread));
        }
    }
}
