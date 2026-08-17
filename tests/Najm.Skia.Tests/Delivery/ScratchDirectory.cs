namespace Najm.Skia.Tests.Delivery;

/// <summary>A per-test temporary directory that deletes itself and everything it holds.</summary>
/// <remarks>
/// Delivery tests are the only ones in the suite that write files, and video and PNG output is
/// exactly the kind of thing that quietly fills a disk. Every fixture that produces output owns one
/// of these and disposes it, so nothing survives a run — passing or failing.
/// </remarks>
internal sealed class ScratchDirectory : IDisposable
{
    internal ScratchDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "najm-delivery-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the absolute directory path.</summary>
    internal string Path { get; }

    /// <summary>Returns an absolute path to a named file inside this directory.</summary>
    internal string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Deletes the directory and its contents, tolerating an already-vanished tree.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover file in the system temp directory must never fail a test that passed.
        }
    }
}
