using System.Reflection;
using System.Security.Cryptography;

namespace Najm.Text;

internal sealed class BundledFontAsset
{
    internal BundledFontAsset(
        string family,
        string version,
        string fileName,
        string resourceName,
        int expectedLength,
        string expectedSha256,
        byte[] bytes)
    {
        Family = family;
        Version = version;
        FileName = fileName;
        ResourceName = resourceName;
        ExpectedLength = expectedLength;
        ExpectedSha256 = expectedSha256;
        Bytes = bytes;
    }

    internal string Family { get; }

    internal string Version { get; }

    internal string FileName { get; }

    internal string ResourceName { get; }

    internal int ExpectedLength { get; }

    internal string ExpectedSha256 { get; }

    internal ReadOnlyMemory<byte> Bytes { get; }
}

internal static class BundledFonts
{
    internal const string ManifestResourceName = "Najm.Text.Fonts.fonts.manifest.json";

    private static readonly Lazy<BundledFontAsset> RomanRegularAsset = CreateLazy(
        family: "Latin Modern Roman",
        version: "2.005",
        fileName: "lmroman10-regular.otf",
        resourceName: "Najm.Text.Fonts.lmroman10-regular.otf",
        expectedLength: 111_536,
        expectedSha256: "1aa18cfefa58132c52ce5de70db1fd1154201c19cd2b2cdaffba4906a33e6852");

    private static readonly Lazy<BundledFontAsset> RomanBoldAsset = CreateLazy(
        family: "Latin Modern Roman",
        version: "2.005",
        fileName: "lmroman10-bold.otf",
        resourceName: "Najm.Text.Fonts.lmroman10-bold.otf",
        expectedLength: 111_240,
        expectedSha256: "102fe06c430a8b681b2bf6876b7cd967ae4d47b4b6b41d915eb7913b726d9fb1");

    private static readonly Lazy<BundledFontAsset> RomanItalicAsset = CreateLazy(
        family: "Latin Modern Roman",
        version: "2.005",
        fileName: "lmroman10-italic.otf",
        resourceName: "Najm.Text.Fonts.lmroman10-italic.otf",
        expectedLength: 118_828,
        expectedSha256: "c1fce25075567bb8dbf2151658c3b442690041db17a2d49fc9e55905ea5b7169");

    private static readonly Lazy<BundledFontAsset> RomanBoldItalicAsset = CreateLazy(
        family: "Latin Modern Roman",
        version: "2.005",
        fileName: "lmroman10-bolditalic.otf",
        resourceName: "Najm.Text.Fonts.lmroman10-bolditalic.otf",
        expectedLength: 118_204,
        expectedSha256: "c37a28eed7a6e03f792b98b5e5f637b2fcda378bb4855f99284f1a88fe35f124");

    private static readonly Lazy<BundledFontAsset> MathAsset = CreateLazy(
        family: "Latin Modern Math",
        version: "1.959",
        fileName: "latinmodern-math.otf",
        resourceName: "Najm.Text.Fonts.latinmodern-math.otf",
        expectedLength: 733_736,
        expectedSha256: "6075562b771f8b82f0c179e363389684f2dd09de30038269e2628e504bd7be0f");

    private static readonly Lazy<IReadOnlyList<BundledFontAsset>> AllAssets = new(
        () => Array.AsReadOnly(
        [
            RomanRegular,
            RomanBold,
            RomanItalic,
            RomanBoldItalic,
            Math,
        ]));

    internal static BundledFontAsset RomanRegular => RomanRegularAsset.Value;

    internal static BundledFontAsset RomanBold => RomanBoldAsset.Value;

    internal static BundledFontAsset RomanItalic => RomanItalicAsset.Value;

    internal static BundledFontAsset RomanBoldItalic => RomanBoldItalicAsset.Value;

    internal static BundledFontAsset Math => MathAsset.Value;

    internal static IReadOnlyList<BundledFontAsset> All => AllAssets.Value;

    internal static string ReadManifestJson()
    {
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(ManifestResourceName)
            ?? throw new InvalidDataException(
                $"The embedded font provenance manifest '{ManifestResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Lazy<BundledFontAsset> CreateLazy(
        string family,
        string version,
        string fileName,
        string resourceName,
        int expectedLength,
        string expectedSha256) =>
        new(() => Load(
            family,
            version,
            fileName,
            resourceName,
            expectedLength,
            expectedSha256));

    private static BundledFontAsset Load(
        string family,
        string version,
        string fileName,
        string resourceName,
        int expectedLength,
        string expectedSha256)
    {
        var assembly = typeof(BundledFonts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                $"The embedded {family} font resource '{resourceName}' is missing.");

        if (stream.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Embedded font '{fileName}' has length {stream.Length}, expected {expectedLength}.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(expectedLength);
        stream.ReadExactly(bytes);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Embedded font '{fileName}' has SHA-256 {actualSha256}, expected {expectedSha256}.");
        }

        return new BundledFontAsset(
            family,
            version,
            fileName,
            resourceName,
            expectedLength,
            expectedSha256,
            bytes);
    }
}
