using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class RenderPassLifecycleTests
{
    [TestMethod]
    public void PublicContextReacquisition_CleansThrowingUnbalancedPass_AndPreservesIdentity()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 1));
        var first = target.GetContext();
        var shape = Rectangle(0f, 0f, 1f, 1f);

        first.PushTransform(Matrix3x2.CreateTranslation(20f, 0f));
        first.PushClip(new Rect(0f, 0f, 1f, 1f));
        first.PushOpacity(0.5f);
        first.DrawPath(shape, Paint.Fill(Color.Srgb(0f, 0f, 1f), isAntialias: false));
        Assert.ThrowsExactly<InvalidOperationException>(first.PopTransform);

        var reacquired = target.GetContext();

        Assert.AreSame(first, reacquired);
        Assert.AreEqual(1f, reacquired.RenderScale);
        Assert.AreEqual(RenderCaps.SkiaSurface, reacquired.Caps);
        Assert.AreEqual(1f, reacquired.Scale);
        reacquired.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        reacquired.DrawPath(shape, Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 255,
                0, 0, 0, 0,
            },
            ReadRgba(target));
    }

    [TestMethod]
    public void InternalPassStamp_BaseScaleAndRenderScaleCancelBeforeAuthorScale()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 2));
        var engineBase = Matrix3x2.CreateScale(2f);

        var context = target.BeginPass(2f, RenderCaps.SkiaSurface, engineBase);

        Assert.AreEqual(2f, context.RenderScale);
        Assert.AreEqual(RenderCaps.SkiaSurface, context.Caps);
        AssertScale(1f, context.Scale);
        context.PushTransform(Matrix3x2.CreateScale(3f, 4f));
        AssertScale(MathF.Sqrt(12f), context.Scale);
        context.PopTransform();
        target.EndPass();

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Scale);
        Assert.AreSame(context, target.GetContext());
        Assert.AreEqual(1f, context.RenderScale);
        AssertScale(1f, context.Scale);
    }

    [TestMethod]
    public void EndPass_UnbalancedStateFailsOnlyAfterCleanup_AndNextPassIsClean()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 2));
        var identity = Matrix3x2.Identity;
        var context = target.BeginPass(1f, RenderCaps.SkiaSurface, identity);
        context.PushTransform(Matrix3x2.CreateScale(2f));
        context.PushClip(new Rect(0f, 0f, 1f, 1f));
        context.PushOpacity(0.5f);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);

        StringAssert.Contains(exception.Message, "3 unbalanced context state push(es)");
        Assert.ThrowsExactly<InvalidOperationException>(() => context.Clear(default));

        var clean = target.BeginPass(1f, RenderCaps.SkiaSurface, identity);
        Assert.AreSame(context, clean);
        AssertScale(1f, clean.Scale);
        Assert.ThrowsExactly<InvalidOperationException>(clean.PopTransform);
        target.EndPass();
        Assert.ThrowsExactly<InvalidOperationException>(target.EndPass);
    }

    [TestMethod]
    public void BeginPass_InvalidStampDoesNotResetTheActivePass()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        context.PushTransform(Matrix3x2.CreateScale(2f));
        var invalidBase = Matrix3x2.Identity;
        invalidBase.M31 = float.NaN;

        foreach (var invalidScale in new[]
                 {
                     0f,
                     -1f,
                     float.NaN,
                     float.PositiveInfinity,
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => target.BeginPass(invalidScale, RenderCaps.SkiaSurface, Matrix3x2.Identity));
            AssertScale(2f, context.Scale);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => target.BeginPass(1f, RenderCaps.SkiaSurface, invalidBase));
        AssertScale(2f, context.Scale);
        Assert.ThrowsExactly<ArgumentException>(
            () => target.BeginPass(1f, RenderCaps.None, Matrix3x2.Identity));
        AssertScale(2f, context.Scale);

        context.PopTransform();
        target.EndPass();
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

    private static void AssertScale(float expected, float actual) =>
        Assert.AreEqual(expected, actual, 1e-5f);
}
