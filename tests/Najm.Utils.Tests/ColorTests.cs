using System.Numerics;
using Najm.Utils;

namespace Najm.Utils.Tests;

[TestClass]
public sealed class ColorTests
{
    [TestMethod]
    public void SrgbStoragePreservesFiniteExtendedChannels()
    {
        var color = Color.Srgb(-0.25f, 1.25f, 0.5f, 0.4f);

        Assert.AreEqual(-0.25f, color.R);
        Assert.AreEqual(1.25f, color.G);
        Assert.AreEqual(0.5f, color.B);
        Assert.AreEqual(0.4f, color.A);
        Assert.IsFalse(color.IsInSrgbGamut);
    }

    [TestMethod]
    public void DefaultAndNamedColorsHaveDocumentedAlpha()
    {
        Assert.AreEqual(new Color(0f, 0f, 0f, 0f), Color.Transparent);
        Assert.AreEqual(1f, Color.Black.A);
        Assert.AreEqual(1f, Color.White.A);
    }

    [TestMethod]
    public void ConstructionRejectsNonFiniteChannelsAndInvalidAlpha()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(float.NaN, 0f, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(0f, float.PositiveInfinity, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(0f, 0f, float.NegativeInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(0f, 0f, 0f, -0.01f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(0f, 0f, 0f, 1.01f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Color(0f, 0f, 0f, float.NaN));
    }

    [TestMethod]
    public void ExplicitGamutClampOnlyChangesRgb()
    {
        var source = new Color(-0.2f, 0.4f, 1.3f, 0.25f);
        var clamped = source.ClampToSrgbGamut();

        Assert.AreEqual(new Color(0f, 0.4f, 1f, 0.25f), clamped);
        Assert.IsTrue(clamped.IsInSrgbGamut);
        Assert.AreEqual(0.25f, clamped.A);
    }

    [TestMethod]
    public void SrgbTransferMatchesReferenceValues()
    {
        Assert.AreEqual(0f, Color.SrgbToLinear(0f));
        Assert.AreEqual(1f, Color.SrgbToLinear(1f), 1e-6f);
        Assert.AreEqual(0.21404114f, Color.SrgbToLinear(0.5f), 1e-7f);
        Assert.AreEqual(0.0031308f, Color.SrgbToLinear(0.04045f), 1e-7f);
        Assert.AreEqual(0.04045f, Color.LinearToSrgb(0.0031308f), 1e-6f);
    }

    [TestMethod]
    public void ExtendedSrgbTransferIsSignPreservingAndRoundTrips()
    {
        foreach (var encoded in new[] { -1.4f, -0.5f, -0.02f, 0f, 0.02f, 0.5f, 1.4f })
        {
            var linear = Color.SrgbToLinear(encoded);
            var roundTrip = Color.LinearToSrgb(linear);

            Assert.AreEqual(encoded, roundTrip, 2e-6f, $"Round-trip failed for {encoded}.");
        }
    }

    [TestMethod]
    public void LinearVectorConversionRoundTripsAndPreservesAlpha()
    {
        var source = new Color(-0.1f, 0.25f, 1.1f, 0.3f);
        Vector3 linear = source.ToLinearSrgb();
        var roundTrip = Color.FromLinearSrgb(linear, source.A);

        AssertColorClose(source, roundTrip, 2e-6f);
    }

    [TestMethod]
    public void HslFactoryProducesReferencePrimariesAndWrapsHue()
    {
        AssertColorClose(new Color(1f, 0f, 0f), Color.Hsl(0d, 1f, 0.5f));
        AssertColorClose(new Color(0f, 1f, 0f), Color.Hsl(120d, 1f, 0.5f));
        AssertColorClose(new Color(0f, 0f, 1f), Color.Hsl(240d, 1f, 0.5f));
        AssertColorClose(Color.Hsl(330d, 1f, 0.5f), Color.Hsl(-30d, 1f, 0.5f));
    }

    [TestMethod]
    public void HslRoundTripCoversChromaticAndAchromaticColors()
    {
        foreach (var source in new[]
                 {
                     new Color(0.12f, 0.34f, 0.78f, 0.6f),
                     new Color(0.4f, 0.4f, 0.4f, 0.2f),
                     Color.Black,
                     Color.White,
                 })
        {
            var (hue, saturation, lightness) = source.ToHsl();
            var roundTrip = Color.Hsl(hue, saturation, lightness, source.A);

            AssertColorClose(source, roundTrip, 2e-6f);
        }
    }

    [TestMethod]
    public void HslConversionNeverSilentlyClampsExtendedRgb()
    {
        var extended = new Color(-0.1f, 0.5f, 1.1f);

        Assert.ThrowsExactly<InvalidOperationException>(() => extended.ToHsl());
    }

    [TestMethod]
    public void OklchConversionOfRedMatchesReference()
    {
        var (lightness, chroma, hue) = new Color(1f, 0f, 0f).ToOkLch();

        Assert.AreEqual(0.6279554f, lightness, 2e-6f);
        Assert.AreEqual(0.2576833f, chroma, 2e-6f);
        Assert.AreEqual(29.2339d, hue.Degrees, 2e-3d);
    }

    [TestMethod]
    public void OklchRoundTripsInGamutAndExtendedSrgb()
    {
        foreach (var source in new[]
                 {
                     new Color(0.15f, 0.6f, 0.85f, 0.7f),
                     new Color(1.1f, -0.05f, 0.4f, 0.3f),
                 })
        {
            var (lightness, chroma, hue) = source.ToOkLch();
            var roundTrip = Color.OkLch(lightness, chroma, hue, source.A);

            AssertColorClose(source, roundTrip, 8e-5f);
        }
    }

    [TestMethod]
    public void HighChromaOklchPreservesOutOfGamutResultUntilExplicitClamp()
    {
        var extended = Color.OkLch(0.7f, 0.4f, 40d);

        Assert.IsFalse(extended.IsInSrgbGamut);
        Assert.AreNotEqual(extended, extended.ClampToSrgbGamut());
        Assert.IsTrue(extended.ClampToSrgbGamut().IsInSrgbGamut);
    }

    [TestMethod]
    public void ColorSpaceFactoriesValidateTheirDomains()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Color.Hsl(0d, -0.1f, 0.5f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Color.Hsl(0d, 0.5f, 1.1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Color.OkLch(0.5f, -0.1f, 0d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Color.OkLch(float.NaN, 0.1f, 0d));
    }

    private static void AssertColorClose(Color expected, Color actual, float tolerance = 1e-6f)
    {
        Assert.AreEqual(expected.R, actual.R, tolerance, "Red channel differs.");
        Assert.AreEqual(expected.G, actual.G, tolerance, "Green channel differs.");
        Assert.AreEqual(expected.B, actual.B, tolerance, "Blue channel differs.");
        Assert.AreEqual(expected.A, actual.A, tolerance, "Alpha channel differs.");
    }
}

