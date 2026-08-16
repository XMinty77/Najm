using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Najm.Core.Tests")]

// The end-to-end render tests live with the raster backend that proves them, and drive the same
// engine-controlled Scene.Load/Stop/Unload commands the Core tests do.
[assembly: InternalsVisibleTo("Najm.Skia.Tests")]
