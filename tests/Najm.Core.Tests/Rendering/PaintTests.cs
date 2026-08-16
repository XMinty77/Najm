using System.Numerics;
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

    [TestMethod]
    public void Default_KeepsStrokeGeometryDefaults_AndCarriesNoBrushOrDash()
    {
        var paint = default(Paint);

        Assert.IsNull(paint.Brush);
        Assert.AreEqual(LineCap.Butt, paint.Cap);
        Assert.AreEqual(LineJoin.Miter, paint.Join);
        Assert.AreEqual(Paint.DefaultMiterLimit, paint.MiterLimit);
        Assert.AreEqual(4f, paint.MiterLimit);
        Assert.IsNull(paint.Dash);
    }

    [TestMethod]
    public void ColorFactories_KeepTheirDocumentedDefaults()
    {
        var fill = Paint.Fill(Color.White);
        var stroke = Paint.Stroke(Color.White, width: 2f);

        Assert.IsNull(fill.Brush);
        Assert.IsNull(stroke.Brush);
        Assert.AreEqual(LineCap.Butt, stroke.Cap);
        Assert.AreEqual(LineJoin.Miter, stroke.Join);
        Assert.AreEqual(4f, stroke.MiterLimit);
        Assert.IsNull(stroke.Dash);
        Assert.AreEqual(1f, fill.StrokeWidth);
    }

    [TestMethod]
    public void BrushFactories_CarryTheBrushAndItsRepresentativeColor()
    {
        var solid = Brush.Solid(Color.Srgb(0.2f, 0.4f, 0.6f));
        var gradient = Brush.Linear(
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            [new GradientStop(0f, Color.Black), new GradientStop(1f, Color.White)]);

        var solidFill = Paint.Fill(solid);
        var gradientStroke = Paint.Stroke(
            gradient,
            width: 3f,
            cap: LineCap.Round,
            join: LineJoin.Bevel,
            miterLimit: 2f,
            dash: new StrokeDash([2f, 1f], phase: 0.5f));

        Assert.AreEqual(solid, solidFill.Brush);
        Assert.AreEqual(Color.Srgb(0.2f, 0.4f, 0.6f), solidFill.Color);
        Assert.AreEqual(PaintStyle.Fill, solidFill.Style);
        Assert.AreEqual(gradient, gradientStroke.Brush);
        Assert.AreEqual(Color.White, gradientStroke.Color);
        Assert.AreEqual(PaintStyle.Stroke, gradientStroke.Style);
        Assert.AreEqual(3f, gradientStroke.StrokeWidth);
        Assert.AreEqual(LineCap.Round, gradientStroke.Cap);
        Assert.AreEqual(LineJoin.Bevel, gradientStroke.Join);
        Assert.AreEqual(2f, gradientStroke.MiterLimit);
        Assert.AreEqual(new StrokeDash([2f, 1f], phase: 0.5f), gradientStroke.Dash);
    }

    [TestMethod]
    public void StrokeGeometry_IsValidated()
    {
        var color = Color.Srgb(1f, 1f, 1f);

        Assert.ThrowsExactly<ArgumentException>(
            () => Paint.Stroke(color, 1f, cap: (LineCap)int.MaxValue));
        Assert.ThrowsExactly<ArgumentException>(
            () => Paint.Stroke(color, 1f, join: (LineJoin)int.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Paint.Stroke(color, 1f, miterLimit: 0.5f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Paint.Stroke(color, 1f, miterLimit: float.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Paint.Stroke(color, 1f, miterLimit: float.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentException>(
            () => Paint.Stroke(color, 1f, dash: default(StrokeDash)));
    }

    [TestMethod]
    public void BrushPaints_ShareTheColorPaintValidation()
    {
        var brush = Brush.Solid(Color.White);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(brush, 0f));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Paint.Stroke(brush, float.NaN));
        Assert.ThrowsExactly<ArgumentException>(
            () => Paint.Fill(brush, blendMode: (BlendMode)int.MaxValue));
        Assert.ThrowsExactly<ArgumentException>(
            () => new Paint(brush, (PaintStyle)int.MaxValue));
    }
}
