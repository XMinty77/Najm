using System.Runtime.CompilerServices;

// The platform translation layers — the key and button maps, the device plumbing — are internal
// because nothing outside this host has a use for them, and wrong because a name matched by
// coincidence rather than by intent. The tests read them directly.
[assembly: InternalsVisibleTo("Najm.Host.Desktop.Tests")]
