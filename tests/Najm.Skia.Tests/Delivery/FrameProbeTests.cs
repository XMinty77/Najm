using Najm.Core;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The reading end of the delivery seam: a frame written to disk, decoded back, and measured
/// without leaving C#.
/// </summary>
/// <remarks>
/// The load-bearing property is the round trip. Everything <see cref="FrameProbe"/> reports is
/// arithmetic that <c>Najm.Core</c> already has tests for; what only this assembly can prove is that
/// the bytes reaching that arithmetic are the bytes the file holds — no colour management, no
/// premultiply, no channel swap. A decode that quietly transformed pixels would make every
/// measurement plausible and wrong, and would break byte-identity checks against a frame this same
/// backend wrote.
/// </remarks>
[TestClass]
public sealed class FrameProbeTests
{
    private static readonly (byte Red, byte Green, byte Blue, byte Alpha)[] Pattern =
    [
        (255, 255, 255, 255),
        (0, 0, 0, 255),
        (200, 100, 50, 255),
        (1, 2, 3, 255),
        (255, 0, 0, 255),
        (0, 255, 0, 255),
        (0, 0, 255, 255),
        (128, 128, 128, 255),
        (17, 34, 51, 255),
        (254, 254, 254, 255),
        (255, 254, 255, 255),
        (0, 1, 0, 255),
    ];

    public TestContext TestContext { get; set; } = null!;

    /// <summary>A frame written by this backend and read back is the same frame, byte for byte.</summary>
    /// <remarks>
    /// This is the check the decoder's "no colour space on the destination info" decision exists to
    /// pass. Naming a colour space there would ask Skia to colour-manage the decode, and an untagged
    /// PNG — which Skia then assumes is sRGB — would come back transformed, so a frame would fail an
    /// identity check against itself.
    /// </remarks>
    [TestMethod]
    public void AWrittenFrameDecodesBackToExactlyTheBytesThatWereWritten()
    {
        using var original = BuildPattern();
        var path = WritePng(original, "roundtrip");

        using var decoded = FrameProbe.Read(path);

        Assert.AreEqual(original.Width, decoded.Width);
        Assert.AreEqual(original.Height, decoded.Height);
        Assert.AreEqual(PixelFormat.Rgba8888, decoded.Format, "Straight alpha is what a PNG stores.");
        Assert.IsTrue(
            FrameComparison.AreIdentical(decoded, original),
            $"The decode transformed the pixels: {FrameComparison.Between(decoded, original)}");
    }

    /// <summary>Measuring a file agrees with measuring the pixels that were written to it.</summary>
    [TestMethod]
    public void MeasuringAFileAgreesWithMeasuringTheFrameItWasWrittenFrom()
    {
        using var original = BuildPattern();
        var path = WritePng(original, "measured");

        var fromFile = FrameProbe.Measure(path);
        var fromMemory = FrameStats.Of(original);

        Assert.AreEqual(fromMemory.PixelCount, fromFile.PixelCount);
        Assert.AreEqual(1L, fromFile.ClippedWhitePixels(), "One pure white pixel in the pattern.");
        Assert.AreEqual(3L, fromFile.ClippedWhitePixels(254), "Plus the two near-whites.");
        Assert.AreEqual(1L, fromFile.CrushedBlackPixels());
        Assert.AreEqual(
            fromMemory.Percentile(FrameChannel.Luma, 0.9d),
            fromFile.Percentile(FrameChannel.Luma, 0.9d));
        Assert.AreEqual(fromMemory.MeanRelativeLuminance, fromFile.MeanRelativeLuminance, 1e-12d);
        Assert.IsTrue(fromFile.AllPixelsOpaque);
    }

    /// <summary>
    /// Two files holding the same image are identical; one changed pixel is found and located.
    /// </summary>
    [TestMethod]
    public void TwoFilesAreComparedByTheirPixelsAndOneChangedPixelIsLocated()
    {
        using var original = BuildPattern();
        var referencePath = WritePng(original, "reference");
        var copyPath = WritePng(original, "copy");

        Assert.IsTrue(FrameProbe.AreIdentical(copyPath, referencePath));
        Assert.IsTrue(FrameProbe.Compare(copyPath, referencePath).AreIdentical);

        using var altered = BuildPattern();
        // (2, 0) is the (200, 100, 50) pixel; nudging blue five levels is the smallest change a
        // grading pass would care about and the largest one a lossy round trip could hide.
        SetPixel(altered, 2, 0, 200, 100, 55);
        var alteredPath = WritePng(altered, "altered");

        Assert.IsFalse(FrameProbe.AreIdentical(alteredPath, referencePath));

        var difference = FrameProbe.Compare(alteredPath, referencePath);

        Assert.AreEqual(1L, difference.DifferingPixels);
        Assert.AreEqual(5, difference.MaxChannelDifference, "Blue moved from 50 to 55.");
        Assert.AreEqual(2, difference.FirstDifferenceX);
        Assert.AreEqual(0, difference.FirstDifferenceY);
    }

    /// <summary>
    /// Differently sized files: false from the identity check, a refusal from the difference report.
    /// </summary>
    [TestMethod]
    public void DifferentlySizedFilesAreNotIdenticalAndCannotBeDifferenced()
    {
        using var wide = PixelFrameLease.Rent(4, 3, PixelFormat.Rgba8888);
        wide.Pixels.Fill(255);
        using var narrow = PixelFrameLease.Rent(3, 3, PixelFormat.Rgba8888);
        narrow.Pixels.Fill(255);

        var widePath = WritePng(wide, "wide");
        var narrowPath = WritePng(narrow, "narrow");

        Assert.IsFalse(FrameProbe.AreIdentical(widePath, narrowPath));
        Assert.ThrowsExactly<ArgumentException>(() => FrameProbe.Compare(widePath, narrowPath));
    }

    /// <summary>A missing or unreadable file fails loudly rather than measuring nothing.</summary>
    [TestMethod]
    public void UnreadableInputsFailLoudly()
    {
        var missing = Path.Combine(TestContext.TestRunDirectory!, "no-such-frame.png");
        Assert.ThrowsExactly<FileNotFoundException>(() => FrameProbe.Read(missing));
        Assert.ThrowsExactly<FileNotFoundException>(() => FrameProbe.Measure(missing));

        var notAnImage = Path.Combine(TestContext.TestRunDirectory!, "not-an-image.png");
        File.WriteAllText(notAnImage, "This is not a PNG, whatever the extension claims.");
        Assert.ThrowsExactly<InvalidDataException>(() => FrameProbe.Read(notAnImage));

        Assert.ThrowsExactly<ArgumentException>(() => FrameProbe.Read("  "));
    }

    private static PixelFrameLease BuildPattern()
    {
        var lease = PixelFrameLease.Rent(4, 3, PixelFormat.Rgba8888);
        lease.Pixels.Clear();
        for (var index = 0; index < Pattern.Length; index++)
        {
            var (red, green, blue, alpha) = Pattern[index];
            SetPixel(lease, index % 4, index / 4, red, green, blue, alpha);
        }

        return lease;
    }

    private static void SetPixel(
        PixelFrameLease lease,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha = 255)
    {
        var row = lease.Row(y);
        var offset = x * 4;
        row[offset] = red;
        row[offset + 1] = green;
        row[offset + 2] = blue;
        row[offset + 3] = alpha;
    }

    private string WritePng(PixelFrameLease pixels, string name)
    {
        var path = Path.Combine(TestContext.TestRunDirectory!, $"{TestContext.TestName}-{name}.png");
        SkiaPngWriter.Write(pixels, path);
        return path;
    }
}
