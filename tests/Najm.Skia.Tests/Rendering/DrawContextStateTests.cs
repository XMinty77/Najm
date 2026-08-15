using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class DrawContextStateTests
{
    [TestMethod]
    public void RasterCapsAndScale_TrackNestedReflectionRotationAndSingularTransforms()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();

        Assert.AreEqual(RenderCaps.SkiaSurface, context.Caps);
        Assert.AreEqual(1f, context.RenderScale);
        AssertScale(1f, context.Scale);

        context.PushTransform(Matrix3x2.CreateScale(4f, 9f));
        AssertScale(6f, context.Scale);
        context.PushTransform(Matrix3x2.CreateRotation(0.37f));
        AssertScale(6f, context.Scale);
        context.PushTransform(Matrix3x2.CreateScale(-1f, 1f));
        AssertScale(6f, context.Scale);
        context.PopTransform();
        context.PopTransform();
        context.PopTransform();
        AssertScale(1f, context.Scale);

        context.PushTransform(new Matrix3x2(0f, 0f, 0f, 1f, 0f, 0f));
        Assert.AreEqual(0f, context.Scale);
        context.PopTransform();
    }

    [TestMethod]
    public void NestedTransform_OrderMatchesSystemNumericsCurrentTimesLocalConvention()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var actualTarget = provider.CreateTarget(new SurfaceSpec(5, 5));
        using var expectedTarget = provider.CreateTarget(new SurfaceSpec(5, 5));
        var actual = actualTarget.GetContext();
        var expected = expectedTarget.GetContext();
        var transparent = Color.Srgb(0f, 0f, 0f, 0f);
        var red = Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false);
        var local = Rectangle(0f, 0f, 2f, 1f);
        var parent = Matrix3x2.CreateTranslation(3f, 1f);
        var child = new Matrix3x2(0f, 1f, -1f, 0f, 0f, 0f);

        actual.Clear(transparent);
        actual.PushTransform(parent);
        actual.PushTransform(child);
        actual.DrawPath(local, red);
        actual.PopTransform();
        actual.PopTransform();

        expected.Clear(transparent);
        expected.DrawPath(Transform(local, child, parent), red);

        CollectionAssert.AreEqual(ReadRgba(expectedTarget), ReadRgba(actualTarget));
    }

    [TestMethod]
    public void Clear_IgnoresCurrentTransform_HonorsClipAndRestoresState()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(3, 1));
        var context = target.GetContext();

        context.Clear(Color.Srgb(0f, 0f, 0f));
        context.PushClip(new Rect(1f, 0f, 1f, 1f));
        context.PushTransform(Matrix3x2.CreateTranslation(100f, 0f));
        context.Clear(Color.Srgb(1f, 0f, 0f));
        context.PopTransform();
        context.PopClip();

        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 255,
                255, 0, 0, 255,
                0, 0, 0, 255,
            },
            ReadRgba(target));
    }

    [TestMethod]
    public void EvenOddPathClip_IsConsumedSynchronouslyAndSurvivesScratchPathReuse()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(5, 5));
        var context = target.GetContext();
        var clip = new PathBuilder(FillRule.EvenOdd, initialCapacity: 10)
            .MoveTo(0f, 0f)
            .LineTo(5f, 0f)
            .LineTo(5f, 5f)
            .LineTo(0f, 5f)
            .Close()
            .MoveTo(1f, 1f)
            .LineTo(4f, 1f)
            .LineTo(4f, 4f)
            .LineTo(1f, 4f)
            .Close();

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.PushClip(clip);
        context.DrawPath(
            Rectangle(0f, 0f, 5f, 5f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        context.PopClip();

        var expected = new byte[5 * 5 * 4];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                if (x == 0 || x == 4 || y == 0 || y == 4)
                {
                    var offset = ((y * 5) + x) * 4;
                    expected[offset] = 255;
                    expected[offset + 3] = 255;
                }
            }
        }

        CollectionAssert.AreEqual(expected, ReadRgba(target));
    }

    [TestMethod]
    public void GroupOpacity_IsAppliedOnceAfterOverlappingChildren()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(3, 1));
        var context = target.GetContext();

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.PushOpacity(0.5f);
        context.DrawPath(
            Rectangle(0f, 0f, 2f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        context.DrawPath(
            Rectangle(1f, 0f, 3f, 1f),
            Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false));
        context.PopOpacity();

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 128,
                0, 0, 255, 128,
                0, 0, 255, 128,
            },
            ReadRgba(target));
    }

    [TestMethod]
    public void NestedOpacity_CompositesCorrectlyOverSemitransparentDestination()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(1, 1));
        var context = target.GetContext();

        context.Clear(Color.Srgb(0f, 1f, 0f, 0.5f));
        context.PushOpacity(0.5f);
        context.PushOpacity(0.5f);
        context.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));
        context.PopOpacity();
        context.PopOpacity();

        using var snapshot = target.Snapshot();
        var actual = new byte[4];
        snapshot.CopyPixels(actual, PixelFormat.Rgba8888Premul);
        CollectionAssert.AreEqual(new byte[] { 64, 96, 0, 160 }, actual);
    }

    [TestMethod]
    public void MismatchedPop_PreservesStrictStack_ThenCorrectPopsRecover()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();

        context.PushTransform(Matrix3x2.CreateScale(4f));
        context.PushClip(new Rect(0f, 0f, 1f, 1f));

        Assert.ThrowsExactly<InvalidOperationException>(context.PopTransform);
        AssertScale(4f, context.Scale);
        context.PopClip();
        AssertScale(4f, context.Scale);
        context.PopTransform();
        AssertScale(1f, context.Scale);

        context.PushOpacity(1f);
        Assert.ThrowsExactly<InvalidOperationException>(context.PopClip);
        context.PopOpacity();

        Assert.ThrowsExactly<InvalidOperationException>(context.PopTransform);
        Assert.ThrowsExactly<InvalidOperationException>(context.PopClip);
        Assert.ThrowsExactly<InvalidOperationException>(context.PopOpacity);
    }

    [TestMethod]
    public void InvalidPushArguments_DoNotCreatePopableState_WhileSingularTransformIsLegal()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var invalid = Matrix3x2.Identity;
        invalid.M21 = float.NaN;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.PushTransform(invalid));
        Assert.ThrowsExactly<InvalidOperationException>(context.PopTransform);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.PushOpacity(float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.PushOpacity(-0.01f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => context.PushOpacity(1.01f));
        Assert.ThrowsExactly<InvalidOperationException>(context.PopOpacity);
        Assert.ThrowsExactly<ArgumentNullException>(() => context.PushClip(null!));
        Assert.ThrowsExactly<InvalidOperationException>(context.PopClip);

        context.PushTransform(new Matrix3x2(0f, 0f, 0f, 0f, 0f, 0f));
        Assert.AreEqual(0f, context.Scale);
        context.PopTransform();
    }

    private static PathBuilder Rectangle(float left, float top, float right, float bottom) =>
        new PathBuilder(initialCapacity: 5)
            .MoveTo(left, top)
            .LineTo(right, top)
            .LineTo(right, bottom)
            .LineTo(left, bottom)
            .Close();

    private static PathBuilder Transform(PathBuilder source, params Matrix3x2[] transforms)
    {
        var result = new PathBuilder(source.FillRule, source.Count);
        foreach (var command in source.Commands)
        {
            if (command.Verb == PathVerb.Close)
            {
                result.Close();
                continue;
            }

            var point = command.Point1;
            foreach (var transform in transforms)
            {
                point = Vector2.Transform(point, transform);
            }

            switch (command.Verb)
            {
                case PathVerb.Move:
                    result.MoveTo(point.X, point.Y);
                    break;
                case PathVerb.Line:
                    result.LineTo(point.X, point.Y);
                    break;
                default:
                    Assert.Fail($"Unexpected test path verb {command.Verb}.");
                    break;
            }
        }

        return result;
    }

    private static byte[] ReadRgba(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[checked(target.Size.Width * target.Size.Height * 4)];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static void AssertScale(float expected, float actual) =>
        Assert.AreEqual(expected, actual, 1e-5f);
}
