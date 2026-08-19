using System.Numerics;

namespace Najm.Utils;

/// <summary>
/// Stores finite, sRGB-encoded red, green, blue, and alpha channels as
/// single-precision values.
/// </summary>
/// <remarks>
/// RGB channels use extended sRGB: finite values below zero and above one are
/// preserved so conversion from wide-gamut spaces never clips silently. Alpha
/// is always finite and in the closed interval [0, 1]; construction fails for
/// invalid alpha. Call <see cref="ClampToSrgbGamut"/> when an explicit gamut
/// boundary operation is required. This descriptor stores straight alpha;
/// rendering backends premultiply only when crossing their API boundary.
/// </remarks>
public readonly struct Color : IEquatable<Color>
{
    private const double SrgbToLinearThreshold = 0.04045d;
    private const double LinearToSrgbThreshold = 0.0031308d;
    private const double AchromaticOklchEpsilon = 1e-7d;

    /// <summary>
    /// Creates an sRGB-referenced color without clipping finite RGB channels.
    /// </summary>
    /// <param name="red">The finite, sRGB-encoded red channel.</param>
    /// <param name="green">The finite, sRGB-encoded green channel.</param>
    /// <param name="blue">The finite, sRGB-encoded blue channel.</param>
    /// <param name="alpha">A finite alpha value in [0, 1].</param>
    public Color(float red, float green, float blue, float alpha = 1f)
    {
        RequireFinite(red, nameof(red));
        RequireFinite(green, nameof(green));
        RequireFinite(blue, nameof(blue));
        RequireUnitInterval(alpha, nameof(alpha));

        R = red;
        G = green;
        B = blue;
        A = alpha;
    }

    /// <summary>Gets the finite, sRGB-encoded red channel.</summary>
    public float R { get; }

    /// <summary>Gets the finite, sRGB-encoded green channel.</summary>
    public float G { get; }

    /// <summary>Gets the finite, sRGB-encoded blue channel.</summary>
    public float B { get; }

    /// <summary>Gets alpha in the closed interval [0, 1].</summary>
    public float A { get; }

    /// <summary>Gets transparent black.</summary>
    /// <remarks>
    /// <para>
    /// This is transparent <em>black</em>: RGB is zero and so is alpha. Under source-over
    /// compositing that is the identity, so as a flat fill it is genuinely "nothing".
    /// </para>
    /// <para>
    /// <strong>It is almost never the right end of a gradient.</strong> Gradient stops interpolate
    /// straight (unpremultiplied) color, so a ramp from red to this value passes through half-alpha
    /// <em>dark</em> red and leaves a grey bruise through what should be a clean fade. Fade with
    /// <see cref="Fade"/> — same RGB, zero alpha — and the ramp stays the color it started as the
    /// whole way out.
    /// </para>
    /// </remarks>
    public static Color Transparent => default;

    /// <summary>Gets opaque black.</summary>
    public static Color Black => new(0f, 0f, 0f);

    /// <summary>Gets opaque white.</summary>
    public static Color White => new(1f, 1f, 1f);

    /// <summary>
    /// Creates an sRGB-referenced color without clipping finite RGB channels.
    /// </summary>
    public static Color Srgb(float red, float green, float blue, float alpha = 1f) =>
        new(red, green, blue, alpha);

    /// <summary>
    /// Gets whether every RGB channel lies inside the conventional sRGB cube.
    /// </summary>
    public bool IsInSrgbGamut =>
        R is >= 0f and <= 1f &&
        G is >= 0f and <= 1f &&
        B is >= 0f and <= 1f;

    /// <summary>
    /// Explicitly clamps RGB to the conventional sRGB cube. Alpha is unchanged.
    /// </summary>
    public Color ClampToSrgbGamut() =>
        new(Math.Clamp(R, 0f, 1f), Math.Clamp(G, 0f, 1f), Math.Clamp(B, 0f, 1f), A);

    /// <summary>Returns this color with a validated replacement alpha.</summary>
    /// <param name="alpha">A finite alpha value in [0, 1].</param>
    public Color WithAlpha(float alpha) => new(R, G, B, alpha);

    /// <summary>Returns this color at zero alpha, keeping its RGB channels.</summary>
    /// <remarks>
    /// <para>
    /// This is <c>WithAlpha(0)</c> under a name that says what it is for: the correct far end of a
    /// fade. Gradient stops interpolate straight (unpremultiplied) color and alpha independently, so
    /// the RGB an author writes at the transparent end is still visible at every partially
    /// transparent sample in between. Fading to <see cref="Transparent"/> drags RGB toward black
    /// along the way and dirties the falloff; fading to <c>Fade()</c> holds the hue and moves only
    /// coverage, which is what a glow, a halo, a vignette, or a feathered edge actually means.
    /// </para>
    /// <para>
    /// The two agree only when the color is already black, which is why the mistake survives a
    /// dark-background check and shows up later.
    /// </para>
    /// <example>
    /// <code>
    /// // Wrong: passes through half-alpha grey.
    /// Brush.Radial(center, r, [new(0f, glow), new(1f, Color.Transparent)]);
    ///
    /// // Right: holds the hue and fades only coverage.
    /// Brush.Radial(center, r, [new(0f, glow), new(1f, glow.Fade())]);
    ///
    /// // Shorter still, and impossible to get wrong.
    /// Brush.RadialFade(center, r, glow);
    /// </code>
    /// </example>
    /// </remarks>
    public Color Fade() => new(R, G, B, 0f);

    /// <summary>
    /// Converts the encoded RGB channels to linear-light extended sRGB.
    /// Alpha is not included and remains available through <see cref="A"/>.
    /// </summary>
    public Vector3 ToLinearSrgb() =>
        new(SrgbToLinear(R), SrgbToLinear(G), SrgbToLinear(B));

    /// <summary>
    /// Creates an encoded extended-sRGB color from finite linear-light channels.
    /// No gamut clipping is performed.
    /// </summary>
    public static Color FromLinearSrgb(Vector3 linearRgb, float alpha = 1f) =>
        new(
            LinearToSrgb(linearRgb.X),
            LinearToSrgb(linearRgb.Y),
            LinearToSrgb(linearRgb.Z),
            alpha);

    /// <summary>
    /// Converts one finite encoded extended-sRGB channel to linear light using
    /// the sign-preserving extension of the sRGB transfer function.
    /// </summary>
    public static float SrgbToLinear(float channel)
    {
        RequireFinite(channel, nameof(channel));
        return ToFiniteFloat(SrgbToLinearCore(channel), "sRGB-to-linear conversion");
    }

    /// <summary>
    /// Converts one finite linear-light channel to encoded extended sRGB using
    /// the sign-preserving extension of the sRGB transfer function.
    /// </summary>
    public static float LinearToSrgb(float channel)
    {
        RequireFinite(channel, nameof(channel));
        return ToFiniteFloat(LinearToSrgbCore(channel), "linear-to-sRGB conversion");
    }

    /// <summary>
    /// Creates an in-gamut sRGB color from HSL using a hue angle.
    /// Saturation and lightness must be finite values in [0, 1]. Hue wraps by
    /// complete turns and is not modified on the supplied <see cref="Angle"/>.
    /// </summary>
    public static Color Hsl(Angle hue, float saturation, float lightness, float alpha = 1f)
    {
        RequireUnitInterval(saturation, nameof(saturation));
        RequireUnitInterval(lightness, nameof(lightness));
        RequireUnitInterval(alpha, nameof(alpha));

        var hueSector = PositiveModulo(hue.Degrees, 360d) / 60d;
        var chroma = (1d - Math.Abs((2d * lightness) - 1d)) * saturation;
        var x = chroma * (1d - Math.Abs((hueSector % 2d) - 1d));
        var offset = lightness - (chroma / 2d);

        var (red, green, blue) = (int)Math.Floor(hueSector) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };

        return FromComputedSrgb(red + offset, green + offset, blue + offset, alpha);
    }

    /// <summary>
    /// Creates an in-gamut sRGB color from HSL with hue expressed in degrees.
    /// </summary>
    public static Color Hsl(double hueDegrees, float saturation, float lightness, float alpha = 1f) =>
        Hsl(Angle.Deg(hueDegrees), saturation, lightness, alpha);

    /// <summary>
    /// Converts an in-gamut color to HSL. Achromatic colors report zero hue.
    /// Extended-sRGB colors fail rather than being silently clamped; call
    /// <see cref="ClampToSrgbGamut"/> first when clipping is intended.
    /// </summary>
    public (Angle Hue, float Saturation, float Lightness) ToHsl()
    {
        if (!IsInSrgbGamut)
        {
            throw new InvalidOperationException(
                "HSL conversion requires RGB inside the sRGB gamut. Clamp explicitly first if desired.");
        }

        var red = (double)R;
        var green = (double)G;
        var blue = (double)B;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var chroma = maximum - minimum;
        var lightness = (maximum + minimum) / 2d;

        if (chroma == 0d)
        {
            return (Angle.Zero, 0f, ToFiniteFloat(lightness, "HSL conversion"));
        }

        var saturation = chroma / (1d - Math.Abs((2d * lightness) - 1d));
        double hueSector;

        if (maximum == red)
        {
            hueSector = ((green - blue) / chroma) % 6d;
        }
        else if (maximum == green)
        {
            hueSector = ((blue - red) / chroma) + 2d;
        }
        else
        {
            hueSector = ((red - green) / chroma) + 4d;
        }

        var hueDegrees = PositiveModulo(hueSector * 60d, 360d);
        return (
            Angle.Deg(hueDegrees),
            ToFiniteFloat(saturation, "HSL conversion"),
            ToFiniteFloat(lightness, "HSL conversion"));
    }

    /// <summary>
    /// Creates an extended-sRGB color from OKLCH using a hue angle.
    /// Lightness may be any finite value, chroma must be finite and non-negative,
    /// and the resulting RGB is preserved even when outside the sRGB gamut.
    /// </summary>
    public static Color OkLch(float lightness, float chroma, Angle hue, float alpha = 1f)
    {
        RequireFinite(lightness, nameof(lightness));
        RequireNonNegativeFinite(chroma, nameof(chroma));
        RequireUnitInterval(alpha, nameof(alpha));

        var labA = chroma * Math.Cos(hue.Radians);
        var labB = chroma * Math.Sin(hue.Radians);

        var lRoot = lightness + (0.3963377774d * labA) + (0.2158037573d * labB);
        var mRoot = lightness - (0.1055613458d * labA) - (0.0638541728d * labB);
        var sRoot = lightness - (0.0894841775d * labA) - (1.2914855480d * labB);

        var l = lRoot * lRoot * lRoot;
        var m = mRoot * mRoot * mRoot;
        var s = sRoot * sRoot * sRoot;

        var linearRed = (4.0767416621d * l) - (3.3077115913d * m) + (0.2309699292d * s);
        var linearGreen = (-1.2684380046d * l) + (2.6097574011d * m) - (0.3413193965d * s);
        var linearBlue = (-0.0041960863d * l) - (0.7034186147d * m) + (1.7076147010d * s);

        return FromComputedSrgb(
            LinearToSrgbCore(linearRed),
            LinearToSrgbCore(linearGreen),
            LinearToSrgbCore(linearBlue),
            alpha);
    }

    /// <summary>
    /// Creates an extended-sRGB color from OKLCH with hue expressed in degrees.
    /// </summary>
    public static Color OkLch(float lightness, float chroma, double hueDegrees, float alpha = 1f) =>
        OkLch(lightness, chroma, Angle.Deg(hueDegrees), alpha);

    /// <summary>
    /// Converts encoded extended sRGB to OKLCH without clipping. Numerically
    /// achromatic colors with chroma at or below 1e-7 report zero chroma and hue.
    /// Alpha remains available through <see cref="A"/>.
    /// </summary>
    public (float Lightness, float Chroma, Angle Hue) ToOkLch()
    {
        var linearRed = SrgbToLinearCore(R);
        var linearGreen = SrgbToLinearCore(G);
        var linearBlue = SrgbToLinearCore(B);

        var l = (0.4122214708d * linearRed) + (0.5363325363d * linearGreen) +
                (0.0514459929d * linearBlue);
        var m = (0.2119034982d * linearRed) + (0.6806995451d * linearGreen) +
                (0.1073969566d * linearBlue);
        var s = (0.0883024619d * linearRed) + (0.2817188376d * linearGreen) +
                (0.6299787005d * linearBlue);

        var lRoot = Math.Cbrt(l);
        var mRoot = Math.Cbrt(m);
        var sRoot = Math.Cbrt(s);

        var lightness = (0.2104542553d * lRoot) + (0.7936177850d * mRoot) -
                        (0.0040720468d * sRoot);
        var labA = (1.9779984951d * lRoot) - (2.4285922050d * mRoot) +
                   (0.4505937099d * sRoot);
        var labB = (0.0259040371d * lRoot) + (0.7827717662d * mRoot) -
                   (0.8086757660d * sRoot);
        var chroma = Math.Sqrt((labA * labA) + (labB * labB));

        if (chroma <= AchromaticOklchEpsilon)
        {
            return (ToFiniteFloat(lightness, "OKLCH conversion"), 0f, Angle.Zero);
        }

        var hue = Math.Atan2(labB, labA);
        if (hue < 0d)
        {
            hue += Math.Tau;
        }

        return (
            ToFiniteFloat(lightness, "OKLCH conversion"),
            ToFiniteFloat(chroma, "OKLCH conversion"),
            Angle.Rad(hue));
    }

    /// <inheritdoc />
    public bool Equals(Color other) =>
        R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <summary>Tests two colors for exact channel equality.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>Tests two colors for exact channel inequality.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    private static Color FromComputedSrgb(double red, double green, double blue, float alpha) =>
        new(
            ToFiniteFloat(red, "color conversion"),
            ToFiniteFloat(green, "color conversion"),
            ToFiniteFloat(blue, "color conversion"),
            alpha);

    private static double SrgbToLinearCore(double channel)
    {
        var sign = channel < 0d ? -1d : 1d;
        var magnitude = Math.Abs(channel);
        var linearMagnitude = magnitude <= SrgbToLinearThreshold
            ? magnitude / 12.92d
            : Math.Pow((magnitude + 0.055d) / 1.055d, 2.4d);
        return sign * linearMagnitude;
    }

    private static double LinearToSrgbCore(double channel)
    {
        var sign = channel < 0d ? -1d : 1d;
        var magnitude = Math.Abs(channel);
        var encodedMagnitude = magnitude <= LinearToSrgbThreshold
            ? magnitude * 12.92d
            : (1.055d * Math.Pow(magnitude, 1d / 2.4d)) - 0.055d;
        return sign * encodedMagnitude;
    }

    private static double PositiveModulo(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0d ? result + modulus : result;
    }

    private static float ToFiniteFloat(double value, string operation)
    {
        if (!double.IsFinite(value) || value is > float.MaxValue or < -float.MaxValue)
        {
            throw new OverflowException($"The {operation} produced a channel outside the finite float range.");
        }

        return (float)value;
    }

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Color components must be finite.");
        }
    }

    private static void RequireUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be finite and in the closed interval [0, 1].");
        }
    }

    private static void RequireNonNegativeFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be finite and non-negative.");
        }
    }
}

