using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Najm.Core.Tests")]

// The end-to-end render tests live with the raster backend that proves them, and drive the same
// engine-controlled Scene.Load/Stop/Unload commands the Core tests do.
[assembly: InternalsVisibleTo("Najm.Skia.Tests")]

// Najm.Lib's nodes only mean anything attached to a loaded scene — a TextNode resolves its
// typesetter and its layer's y-axis orientation at attach — so their tests drive the same
// engine-controlled load commands.
[assembly: InternalsVisibleTo("Najm.Lib.Tests")]
