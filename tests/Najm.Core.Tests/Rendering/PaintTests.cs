using Najm.Utils;

namespace Najm.Core.Tests.Rendering;

[TestClass]
public sealed class PaintTests
{
    [TestMethod]
    public void Default_IsTransparentAntialiasedFill_WithIgnoredZeroStrokeWidth()
    {
        var paint = default(Paint);

        Assert.AreEqual(default, paint.Color);
        Assert.AreEqual(PaintStyle.Fill, paint.Style);
        Assert.AreEqual(0f, paint.StrokeWidth);
        Assert.IsTrue(paint.IsAntialias);
        Assert.AreEqual(BlendMode.SrcOver, paint.BlendMode);
    }

    [TestMethod]
    public void FillAndStroke_AreDistinctValueDescriptors()
    {
        var color = Color.Srgb(0.2f, 0.4f, 0.6f, 0.8f);

        var fill = Paint.Fill(color);
        var stroke = Paint.Stroke(
            color,
            width: 3f,
            isAntialias: false,
            blendMode: BlendMode.Multiply);

        Assert.AreEqual(PaintStyle.Fill, fill.Style);
        Assert.IsTrue(fill.IsAntialias);
        Assert.AreEqual(PaintStyle.Stroke, stroke.Style);
        Assert.AreEqual(3f, stroke.StrokeWidth);
        Assert.IsFalse(stroke.IsAntialias);
        Assert.AreEqual(BlendMode.Multiply, stroke.BlendMode);
    }

    [TestMethod]
    public void StrokeWidth_MustBeFiniteAndPositive()
    {
        var color = Color.Srgb(1f, 1f, 1f);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(color, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(color, -1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(color, float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(color, float.PositiveInfinity));
    }

    [TestMethod]
    public void FillWidth_MayBeZero_ButMustRemainFiniteAndNonnegative()
    {
        var color = Color.Srgb(1f, 1f, 1f);

        var fill = new Paint(color, PaintStyle.Fill, strokeWidth: 0f);

        Assert.AreEqual(0f, fill.StrokeWidth);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Paint(color, PaintStyle.Fill, strokeWidth: -1f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Paint(color, PaintStyle.Fill, strokeWidth: float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Paint(color, PaintStyle.Fill, strokeWidth: float.NegativeInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Paint(color, PaintStyle.Fill, strokeWidth: float.PositiveInfinity));
    }

    [TestMethod]
    public void BlendMode_MustBeDefined()
    {
        var color = Color.Srgb(1f, 1f, 1f);

        Assert.ThrowsExactly<ArgumentException>(
            () => Paint.Fill(color, blendMode: (BlendMode)int.MaxValue));
    }
}
