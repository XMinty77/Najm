using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class EngineTransformSeamTests
{
    [TestMethod]
    public void SetEngineTransform_TranslationMovesDrawnPixelsByTheExactOffset()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 1));
        var context = target.GetContext();
        var unit = Rectangle(0f, 0f, 1f, 1f);
        var red = Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false);
        var transparent = Color.Srgb(0f, 0f, 0f, 0f);

        context.Clear(transparent);
        context.DrawPath(unit, red);

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 255,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
            },
            ReadRgba(target),
            "The identity engine transform must draw the unit path in pixel column zero.");

        context.SetEngineTransform(Matrix3x2.CreateTranslation(2f, 0f));
        context.Clear(transparent);
        context.DrawPath(unit, red);

        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 0,
                0, 0, 0, 0,
                255, 0, 0, 255,
                0, 0, 0, 0,
            },
            ReadRgba(target),
            "A two-unit engine translation must shift the same path exactly two pixel columns in +X.");
        Assert.AreEqual(1f, context.RenderScale, "Setting the engine transform must not restamp the render scale.");
        Assert.AreEqual(1f, context.Scale, 1e-5f, "A pure translation must leave the local-to-virtual scale at one.");

        context.SetEngineTransform(Matrix3x2.CreateTranslation(1f, 0f));
        context.Clear(transparent);
        context.DrawPath(unit, red);

        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 0,
                255, 0, 0, 255,
                0, 0, 0, 0,
                0, 0, 0, 0,
            },
            ReadRgba(target),
            "The second call must replace the engine transform wholesale, landing on column one rather than accumulating onto column three.");
    }

    [TestMethod]
    public void PushTransform_ComposesBelowTheEngineTransform_NotAboveIt()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(5, 2));
        var context = target.GetContext();
        var unit = Rectangle(0f, 0f, 1f, 1f);
        var red = Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false);

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.SetEngineTransform(Matrix3x2.CreateScale(2f));
        context.PushTransform(Matrix3x2.CreateTranslation(1f, 0f));
        Assert.AreEqual(
            2f,
            context.Scale,
            1e-5f,
            "The pushed translation contributes no scale, so the engine scale of two is reported against the unit render scale.");
        context.DrawPath(unit, red);
        context.PopTransform();

        // local × engine: (0,0)-(1,1) translates to (1,0)-(2,1), then scales to (2,0)-(4,2).
        // The rejected order engine × local would give (0,0)-(2,2) then (1,0)-(3,2).
        var expected = new byte[5 * 2 * 4];
        for (var y = 0; y < 2; y++)
        {
            for (var x = 2; x < 4; x++)
            {
                var offset = ((y * 5) + x) * 4;
                expected[offset] = 255;
                expected[offset + 3] = 255;
            }
        }

        CollectionAssert.AreEqual(expected, ReadRgba(target));
    }

    [TestMethod]
    public void SetEngineTransform_ThrowsWhileAuthorStateIsOutstanding_AndLeavesTheStackIntact()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(4, 1));
        var context = target.GetContext();
        var replacement = Matrix3x2.CreateTranslation(2f, 0f);

        context.PushTransform(Matrix3x2.CreateScale(4f));
        var single = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.SetEngineTransform(replacement));
        StringAssert.Contains(single.Message, "1 unbalanced context state push(es)");
        StringAssert.Contains(single.Message, "(transform)");

        context.PushClip(new Rect(0f, 0f, 1f, 1f));
        context.PushOpacity(0.5f);
        var several = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.SetEngineTransform(replacement));
        StringAssert.Contains(several.Message, "3 unbalanced context state push(es)");
        StringAssert.Contains(several.Message, "(transform, clip, opacity)");

        // The stack survives the rejection: the strict LIFO order still unwinds, and the engine
        // transform the failed call would have installed was never applied.
        Assert.ThrowsExactly<InvalidOperationException>(context.PopClip);
        context.PopOpacity();
        context.PopClip();
        Assert.AreEqual(4f, context.Scale, 1e-5f);
        context.PopTransform();
        Assert.AreEqual(1f, context.Scale, 1e-5f);

        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 255,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
            },
            ReadRgba(target),
            "The rejected engine transform must not have been installed.");

        context.SetEngineTransform(replacement);
    }

    [TestMethod]
    public void SetEngineTransform_RejectsNonFiniteMatrices_AndInactivePasses()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = (SkiaRenderTarget)provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var invalid = Matrix3x2.Identity;
        invalid.M32 = float.PositiveInfinity;

        var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => context.SetEngineTransform(invalid));
        Assert.AreEqual("engineToDevice", rejected.ParamName);
        Assert.AreEqual(1f, context.Scale, 1e-5f);

        target.EndPass();
        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.SetEngineTransform(Matrix3x2.Identity));
    }

    [TestMethod]
    public void GetContext_RejectsNonPositiveAndNonFiniteRenderScales()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));

        foreach (var invalidScale in new[]
                 {
                     0f,
                     -1f,
                     float.NaN,
                     float.PositiveInfinity,
                     float.NegativeInfinity,
                 })
        {
            var rejected = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => target.GetContext(invalidScale));
            Assert.AreEqual("renderScale", rejected.ParamName);
        }

        Assert.AreEqual(1f, target.GetContext().RenderScale);
    }

    [TestMethod]
    public void GetContextAtTwoTimes_MakesAUnitPathCoverFourPixels()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext(2f);

        Assert.AreEqual(2f, context.RenderScale);
        Assert.AreEqual(1f, context.Scale, 1e-5f, "The render scale must divide out of the local-to-virtual scale.");
        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 255, 255, 0, 0, 255,
                255, 0, 0, 255, 255, 0, 0, 255,
            },
            ReadRgba(target),
            "At a render scale of two the one-by-one path must cover the whole two-by-two surface.");

        Assert.AreSame(context, target.GetContext());
        Assert.AreEqual(1f, target.GetContext().RenderScale, "Reacquisition at unit scale must restamp the pass.");
        context.Clear(Color.Srgb(0f, 0f, 0f, 0f));
        context.DrawPath(
            Rectangle(0f, 0f, 1f, 1f),
            Paint.Fill(Color.Srgb(1f, 0f, 0f), isAntialias: false));

        CollectionAssert.AreEqual(
            new byte[]
            {
                255, 0, 0, 255, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0,
            },
            ReadRgba(target));
    }

    [TestMethod]
    public void WarmSetEngineTransformLoop_AllocatesNoManagedBytes()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(2, 2));
        var context = target.GetContext();
        var path = Rectangle(0f, 0f, 2f, 2f);
        var paint = Paint.Fill(Color.Srgb(0.25f, 0.5f, 0.75f));
        var engineToDevice = Matrix3x2.CreateScale(1.5f) * Matrix3x2.CreateTranslation(0.25f, -0.5f);

        context.SetEngineTransform(engineToDevice);
        context.DrawPath(path, paint);

        AllocationProbe.AssertNoneAllocated(
            1_000,
            () =>
            {
                context.SetEngineTransform(engineToDevice);
                context.DrawPath(path, paint);
            },
            "The warmed engine transform seam");
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
}
