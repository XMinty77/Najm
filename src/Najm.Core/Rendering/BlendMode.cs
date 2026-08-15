namespace Najm.Core;

/// <summary>Specifies the portable blend subset shared by every rendering backend.</summary>
public enum BlendMode
{
    /// <summary>Composites the source over the destination.</summary>
    SrcOver,

    /// <summary>Multiplies source and destination color channels.</summary>
    Multiply,

    /// <summary>Screens source and destination color channels.</summary>
    Screen,

    /// <summary>Applies multiply or screen according to the destination.</summary>
    Overlay,

    /// <summary>Selects the darker source or destination channel.</summary>
    Darken,

    /// <summary>Selects the lighter source or destination channel.</summary>
    Lighten,

    /// <summary>Brightens the destination to reflect the source.</summary>
    ColorDodge,

    /// <summary>Darkens the destination to reflect the source.</summary>
    ColorBurn,

    /// <summary>Applies multiply or screen according to the source.</summary>
    HardLight,

    /// <summary>Applies a softer lightening or darkening blend.</summary>
    SoftLight,

    /// <summary>Uses the absolute difference between source and destination.</summary>
    Difference,

    /// <summary>Uses a lower-contrast difference blend.</summary>
    Exclusion,

    /// <summary>Adds premultiplied source and destination components with saturation.</summary>
    Plus,
}
