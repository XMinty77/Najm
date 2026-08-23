using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Fractal;

/// <summary>Every number that decides how the clip looks, in one place.</summary>
internal static class Design
{
    /// <summary>The scene's virtual resolution, and the texture extent, and the output size.</summary>
    public static PixelSize Frame => new(1920, 1080);

    /// <summary>The fixed presentation rate.</summary>
    public const double Fps = 60d;

    /// <summary>The clip length in seconds.</summary>
    public const double ClipSeconds = 13d;

    /// <summary>The surrounding colour. The shader's own deep tone, so the vignette has nowhere to seam.</summary>
    public static Color Background => Color.Srgb(0.024f, 0.028f, 0.043f);

    /// <summary>The vignette's corner darkening, painted as a fading disc in <see cref="BlendMode.Multiply"/>.</summary>
    public static Color VignetteTint => Color.Srgb(0.05f, 0.06f, 0.10f, 0.62f);

    /// <summary>The instrument's ink. Deliberately close to the background: it is a footnote.</summary>
    public static Color InstrumentInk => Color.Srgb(0.78f, 0.83f, 0.90f);

    /// <summary>The instrument's warm accent, used only for the live iteration reading.</summary>
    public static Color InstrumentAccent => Color.Srgb(0.95f, 0.76f, 0.36f);
}
