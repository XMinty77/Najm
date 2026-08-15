using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class ImageDrawingTests
{
    private const string RotatedNearestExpectedRgba =
        "00000000" + "00000000" + "00000000" + "00000000" + "00000000" +
        "00000000" + "00000000" + "0000ffff" + "ff0000ff" + "00000000" +
        "00000000" + "00000000" + "ffff00ff" + "00ff00ff" + "00000000" +
        "00000000" + "00000000" + "00000000" + "00000000" + "00000000";

    [TestMethod]
    public void AffineNearestDraw_RotatesAsymmetricImageIntoExactRawRgbaGolden()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var sourceTarget = CreateFourColorSource(provider);
        using var image = sourceTarget.Snapshot();
        using var destination = provider.CreateTarget(new SurfaceSpec(5, 4));
        var context = destination.GetContext();
        var imageToLocal = new Matrix3x2(0f, 1f, -1f, 0f, 4f, 1f);

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawImage(image, imageToLocal, ImageSampling.Nearest);

        Assert.AreEqual(RotatedNearestExpectedRgba, Convert.ToHexString(ReadRgba(destination)).ToLowerInvariant());
        Assert.AreEqual(1f, context.Scale, "The image draw's internal save must restore context state.");
    }

    [TestMethod]
    public void NestedCurrentTransformAndImageToLocal_MatchPrecomposedSystemNumericsMatrix()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var sourceTarget = CreateFourColorSource(provider);
        using var image = sourceTarget.Snapshot();
        using var nestedTarget = provider.CreateTarget(new SurfaceSpec(6, 5));
        using var combinedTarget = provider.CreateTarget(new SurfaceSpec(6, 5));
        var nested = nestedTarget.GetContext();
        var combined = combinedTarget.GetContext();
        var transparent = Color.Srgb(0f, 0f, 0f, 0f);
        var current = Matrix3x2.CreateTranslation(1f, 1f);
        var imageToLocal = new Matrix3x2(1f, 0f, 0.5f, 1f, 0f, 0f);

        nested.Clear(transparent);
        nested.PushTransform(current);
        nested.DrawImage(image, imageToLocal, ImageSampling.Nearest);
        nested.PopTransform();

        combined.Clear(transparent);
        var imageToDevice = imageToLocal * current;
        combined.DrawImage(image, imageToDevice, ImageSampling.Nearest);

        CollectionAssert.AreEqual(ReadRgba(combinedTarget), ReadRgba(nestedTarget));
    }

    [TestMethod]
    public void DefaultSampling_IsLinearAndDiffersFromNearestDuringUpscale()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var source = provider.CreateTarget(new SurfaceSpec(2, 1));
        var sourceContext = source.GetContext();
        sourceContext.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        sourceContext.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        sourceContext.DrawPath(
            Rectangle(1f, 0f, 2f, 1f),
            Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false));
        using var image = source.Snapshot();
        using var defaultTarget = provider.CreateTarget(new SurfaceSpec(4, 1));
        using var linearTarget = provider.CreateTarget(new SurfaceSpec(4, 1));
        using var nearestTarget = provider.CreateTarget(new SurfaceSpec(4, 1));
        var transform = Matrix3x2.CreateScale(2f, 1f);

        defaultTarget.GetContext().DrawImage(image, transform);
        linearTarget.GetContext().DrawImage(image, transform, ImageSampling.Linear);
        nearestTarget.GetContext().DrawImage(image, transform, ImageSampling.Nearest);

        var defaultPixels = ReadRgba(defaultTarget);
        CollectionAssert.AreEqual(defaultPixels, ReadRgba(linearTarget));
        Assert.IsFalse(
            defaultPixels.AsSpan().SequenceEqual(ReadRgba(nearestTarget)),
            "Linear interpolation must not silently lower as nearest-neighbor sampling.");
    }

    [TestMethod]
    public void LinearF16Snapshot_IsColorConvertedWhenDrawnIntoSrgbTarget()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var source = provider.CreateTarget(
            new SurfaceSpec(1, 1, colorSpace: ColorSpace.LinearSrgb));
        source.GetContext().Clear(Color.Srgb(0.75f, 0.5f, 0.25f, 0.5f));
        using var image = source.Snapshot();
        using var destination = provider.CreateTarget(
            new SurfaceSpec(1, 1, colorSpace: ColorSpace.Srgb));

        destination.GetContext().DrawImage(image, Matrix3x2.Identity, ImageSampling.Nearest);

        var actual = ReadRgba(destination);
        AssertByteWithin(191, actual[0], 1, "red");
        AssertByteWithin(128, actual[1], 1, "green");
        AssertByteWithin(64, actual[2], 1, "blue");
        AssertByteWithin(128, actual[3], 1, "alpha");
    }

    [TestMethod]
    public void DrawImage_ValidatesBeforeCanvasMutation_AndDoesNotRetainSource()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        using var source = CreateFourColorSource(provider);
        var context = target.GetContext();
        var invalidMatrix = Matrix3x2.Identity;
        invalidMatrix.M32 = float.PositiveInfinity;

        Assert.ThrowsExactly<ArgumentNullException>(
            () => context.DrawImage(null!, Matrix3x2.Identity));
        Assert.ThrowsExactly<ArgumentException>(
            () => context.DrawImage(new ForeignImage(), Matrix3x2.Identity));
        using var invalidMatrixImage = source.Snapshot();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.DrawImage(invalidMatrixImage, invalidMatrix));

        using (var image = source.Snapshot())
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => context.DrawImage(image, Matrix3x2.Identity, (ImageSampling)int.MaxValue));
        }

        var disposedImage = source.Snapshot();
        disposedImage.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => context.DrawImage(disposedImage, Matrix3x2.Identity));

        using (var image = source.Snapshot())
        {
            context.DrawImage(image, default(Matrix3x2), ImageSampling.Nearest);
            context.DrawImage(image, Matrix3x2.Identity, ImageSampling.Nearest);
            image.Dispose();
        }

        Assert.AreEqual(1f, context.Scale);
        CollectionAssert.AreEqual(ReadRgba(source), ReadRgba(target));
    }

    private static IRenderTarget CreateFourColorSource(ISurfaceProvider provider)
    {
        var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        context.DrawPath(
            Rectangle(1f, 0f, 2f, 1f),
            Paint.Fill(Color.Srgb(0f, 1f, 0f), isAntialias: false));
        context.DrawPath(
            Rectangle(0f, 1f, 1f, 2f),
            Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false));
        context.DrawPath(
            Rectangle(1f, 1f, 2f, 2f),
            Paint.Fill(Color.Srgb(1f, 1f, 0f), isAntialias: false));
        return target;
    }

    private static PathBuilder Rectangle(float left, float top, float right, float bottom) =>
        new PathBuilder(initialCapacity: 5)
            .MoveTo(left, top)
            .LineTo(right, top)
            .LineTo(right, bottom)
            .LineTo(left, bottom)
            .Close();

    private static byte[] ReadRgba(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[checked(target.Size.Width * target.Size.Height * 4)];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static void AssertByteWithin(byte expected, byte actual, byte tolerance, string channel)
    {
        var delta = Math.Abs(expected - actual);
        Assert.IsLessThanOrEqualTo(
            tolerance,
            delta,
            $"Expected {channel}={expected}±{tolerance}, actual {actual}.");
    }

    private sealed class ForeignImage : IImage
    {
        public PixelSize Size => new(1, 1);

        public void CopyPixels(Span<byte> destination, PixelFormat format) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
