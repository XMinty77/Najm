using System.Security.Cryptography;
using System.Text.Json;

namespace Najm.Text.Tests.Fonts;

[TestClass]
public sealed class BundledFontManifestTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void EmbeddedFonts_MatchPinnedLengthsHashesAndManifest()
    {
        using var manifest = JsonDocument.Parse(BundledFonts.ReadManifestJson());
        var manifestFiles = ReadManifestFiles(manifest.RootElement);

        Assert.HasCount(5, BundledFonts.All);
        Assert.HasCount(5, manifestFiles);
        foreach (var asset in BundledFonts.All)
        {
            Assert.AreEqual(asset.ExpectedLength, asset.Bytes.Length, asset.FileName);
            var actualHash = Convert.ToHexString(SHA256.HashData(asset.Bytes.Span)).ToLowerInvariant();
            Assert.AreEqual(asset.ExpectedSha256, actualHash, asset.FileName);

            Assert.IsTrue(manifestFiles.TryGetValue(asset.FileName, out var manifestFile), asset.FileName);
            Assert.AreEqual(asset.ResourceName, manifestFile.ResourceName, asset.FileName);
            Assert.AreEqual(asset.ExpectedLength, manifestFile.Length, asset.FileName);
            Assert.AreEqual(asset.ExpectedSha256, manifestFile.Sha256, asset.FileName);
        }
    }

    [TestMethod]
    public void ProvenanceManifest_PinsAuthoritativeCtanArchives()
    {
        using var manifest = JsonDocument.Parse(BundledFonts.ReadManifestJson());
        var families = manifest.RootElement.GetProperty("families").EnumerateArray().ToArray();

        Assert.HasCount(2, families);
        AssertFamily(
            families[0],
            "Latin Modern Roman",
            "2.005",
            "https://mirrors.ctan.org/fonts/lm.zip",
            "71c48809cb50fbfe09c8eddaa251398957c7b243acdf69f7f807268f0d42c939");
        AssertFamily(
            families[1],
            "Latin Modern Math",
            "1.959",
            "https://mirrors.ctan.org/fonts/lm-math.zip",
            "3d906317f27279af05eb095aa4db5e7f3f87312e69d672e8f8928b64adcd403c");
    }

    [TestMethod]
    public void UpstreamDocuments_MatchPinnedLengthsHashesAndManifestInventory()
    {
        using var manifest = JsonDocument.Parse(BundledFonts.ReadManifestJson());
        var fontsRoot = Path.Combine(RepositoryRoot, "src", "Najm.Text", "Fonts");
        var manifestPaths = new List<string>();

        foreach (var family in manifest.RootElement.GetProperty("families").EnumerateArray())
        {
            foreach (var document in family.GetProperty("documents").EnumerateArray())
            {
                var relativePath = document.GetProperty("file").GetString()
                    ?? throw new InvalidDataException("A manifest document path is null.");
                manifestPaths.Add(relativePath);

                var fullPath = ResolveContainedPath(fontsRoot, relativePath);
                Assert.IsTrue(File.Exists(fullPath), relativePath);
                Assert.AreEqual(
                    document.GetProperty("length").GetInt64(),
                    new FileInfo(fullPath).Length,
                    relativePath);
                using var stream = File.OpenRead(fullPath);
                var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                Assert.AreEqual(
                    document.GetProperty("sha256").GetString(),
                    actualHash,
                    relativePath);
            }
        }

        var onDiskPaths = Directory
            .EnumerateFiles(fontsRoot, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(fontsRoot, path).Replace('\\', '/'))
            .ToArray();

        Assert.HasCount(6, manifestPaths);
        Assert.HasCount(6, onDiskPaths);
        CollectionAssert.AreEquivalent(manifestPaths, onDiskPaths);
    }

    private static Dictionary<string, ManifestFontFile> ReadManifestFiles(JsonElement root)
    {
        var files = new Dictionary<string, ManifestFontFile>(StringComparer.Ordinal);
        foreach (var family in root.GetProperty("families").EnumerateArray())
        {
            foreach (var file in family.GetProperty("files").EnumerateArray())
            {
                var relativePath = file.GetProperty("file").GetString()
                    ?? throw new InvalidDataException("A manifest font path is null.");
                var fileName = Path.GetFileName(relativePath);
                files.Add(
                    fileName,
                    new ManifestFontFile(
                        file.GetProperty("embeddedResource").GetString()
                            ?? throw new InvalidDataException($"The resource name for '{fileName}' is null."),
                        file.GetProperty("length").GetInt32(),
                        file.GetProperty("sha256").GetString()
                            ?? throw new InvalidDataException($"The hash for '{fileName}' is null.")));
            }
        }

        return files;
    }

    private static void AssertFamily(
        JsonElement family,
        string name,
        string version,
        string source,
        string archiveSha256)
    {
        Assert.AreEqual(name, family.GetProperty("name").GetString());
        Assert.AreEqual(version, family.GetProperty("version").GetString());
        Assert.AreEqual(source, family.GetProperty("source").GetString());
        Assert.AreEqual(archiveSha256, family.GetProperty("archiveSha256").GetString());
        Assert.IsGreaterThan(0, family.GetProperty("documents").GetArrayLength());
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, comparison))
        {
            throw new InvalidDataException($"Manifest path '{relativePath}' escapes the font asset root.");
        }

        return fullPath;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Najm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }

    private readonly record struct ManifestFontFile(string ResourceName, int Length, string Sha256);
}
