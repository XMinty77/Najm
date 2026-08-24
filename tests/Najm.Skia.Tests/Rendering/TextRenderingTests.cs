using System.Numerics;
using Najm.Core;
using Najm.Core.Text;
using Najm.Lib;
using Najm.Utils;
using CoreTypesetter = Najm.Text.Typesetter;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// The pixel half of NAJM-TEXT I.9's upright rule, plus the two routes §7.6 makes
/// <see cref="IDrawContext2D.DrawText"/> take.
/// </summary>
/// <remarks>
/// <para>
/// The bounds half of the upright rule is proved in <c>Najm.Lib.Tests</c>, in the node's own local
/// frame. It cannot be the whole proof: bounds could be corrected while the glyphs were still drawn
/// mirrored, or — the subtler failure — both could be flipped the wrong way <em>consistently</em>,
/// which agrees with itself and reads upside down. These tests render actual glyphs through an
/// actual camera and look at where the ink lands.
/// </para>
/// <para>
/// The scene geometry is chosen so the two layer kinds must agree exactly. A screen layer's
/// coordinates are virtual coordinates, so a node at the frame centre draws there. A world layer's
/// default camera maps world <c>(x, y)</c> to virtual <c>(W/2 + x, H/2 − y)</c>, so a node at world
/// origin draws at the same place. Same string, same size, same device point — and therefore the
/// same pixels, if and only if the rule is right in both.
/// </para>
/// </remarks>
[TestClass]
public sealed class TextRenderingTests
{
    private const int Width = 200;
    private const int Height = 120;
    private const float Size = 60f;

    [TestMethod]
    public void TheSameGlyphIsPixelIdenticalInAScreenLayerAndAWorldLayer()
    {
        var screen = RenderScreen("L");
        var world = RenderWorld("L");

        // If the node did not compose the reading frame into local space, the world layer's camera
        // flip would mirror the glyph about its baseline and these two would differ on every row
        // that has ink. They are byte-compared rather than compared loosely for that reason: an
        // upright rule that is only nearly right is not right.
        CollectionAssert.AreEqual(
            screen,
            world,
            "A glyph must read identically in a Y-down screen layer and a Y-up world layer.");
    }

    [TestMethod]
    public void InkSitsAboveTheBaselineInBothLayerKinds()
    {
        // 'L' has no descender, so every one of its pixels must land above the baseline. The
        // baseline is at the frame centre in both scenes by construction, so "above" is "at a
        // smaller device row than Height / 2".
        //
        // This is the assertion the pixel-equality test above cannot make on its own: two renders
        // that were both flipped the wrong way would still be equal to each other.
        foreach (var (name, pixels) in new[] { ("screen", RenderScreen("L")), ("world", RenderWorld("L")) })
        {
            var ink = InkRows(pixels);
            Assert.IsNotNull(ink, $"The {name} layer drew no ink at all.");
            Assert.IsLessThan(
                Height / 2,
                ink.Value.Bottom,
                $"In the {name} layer, 'L' must sit entirely above its baseline at row {Height / 2}.");
        }
    }

    [TestMethod]
    public void AnAuthorsOwnVerticalFlipStillMirrorsTheText()
    {
        var upright = RenderWorld("L");
        var mirrored = RenderWorld("L", node => node.Scale = new Vector2(1f, -1f));

        // I.9's last consequence, and the one that says the rule is a correction rather than a
        // clamp: the node corrects for its layer, not for its author. A deliberate Scale(1, −1)
        // composes below the reading-frame transform and mirrors the text, exactly as it would
        // mirror any other drawable.
        CollectionAssert.AreNotEqual(upright, mirrored, "An author's own vertical flip must still mirror.");

        var uprightInk = InkRows(upright);
        var mirroredInk = InkRows(mirrored);
        Assert.IsNotNull(uprightInk);
        Assert.IsNotNull(mirroredInk);
        Assert.IsLessThan(Height / 2, uprightInk.Value.Bottom, "Upright 'L' is above the baseline.");
        Assert.IsGreaterThanOrEqualTo(
            Height / 2,
            mirroredInk.Value.Top,
            "Mirrored 'L' hangs from the baseline row downward.");
    }

    [TestMethod]
    public void TheOutlineRouteAndTheBlobRouteAgreeOnGeometryAndDifferOnlyAtTheEdges()
    {
        using var typesetter = new CoreTypesetter();
        var layout = typesetter.Typeset(new TypesetRequest("Najm", new Style { Size = Size }));

        var viaBlob = RenderLayout(layout, outlines: false);
        var viaOutlines = RenderLayout(layout, outlines: true);

        // §7.6 requires glyphs to export as filled outlines so publication output does not depend on
        // a viewer's installed fonts. That is only honest if the outlines are the same shape the
        // raster route draws — but the two are rasterized by different Skia code (a blob goes
        // through the glyph mask cache, a path through the scan converter), so they are *not*
        // bit-identical and asserting that they were would be a test that fails for the wrong
        // reason. What they must agree on is the geometry.
        //
        // The load-bearing assertion is the first one. A pixel that is fully covered in one route
        // must not be fully uncovered in the other, and vice versa: that is what "same shape" means
        // in pixels, and it holds exactly. Measured on this sample: 1749 inked pixels, 755 of them
        // fully interior, 993 differing at all — every one of those an edge pixel with partial
        // coverage in at least one route, and none a solid-versus-solid disagreement.
        var solidDisagreements = 0;
        var differing = 0;
        var inked = 0;
        long blobMass = 0;
        long outlineMass = 0;
        for (var index = 0; index < viaBlob.Length; index += 4)
        {
            var a = viaBlob[index];
            var b = viaOutlines[index];
            blobMass += 255 - a;
            outlineMass += 255 - b;
            if (a != 255 || b != 255)
            {
                inked++;
            }

            if (a == b)
            {
                continue;
            }

            differing++;
            if (a is 0 or 255 && b is 0 or 255)
            {
                solidDisagreements++;
            }
        }

        Assert.IsGreaterThan(200, inked, "The sample must actually draw something to compare.");
        Assert.AreEqual(
            0,
            solidDisagreements,
            $"{solidDisagreements} pixels are solid in one route and the opposite solid in the " +
            "other, which means the two routes disagree about the glyph outline itself rather than " +
            "about its antialiasing.");
        Assert.IsLessThan(
            inked,
            differing,
            "Every differing pixel must be an edge pixel, so the differences cannot outnumber the ink.");

        // The residue is antialiasing coverage, and it is bounded: the scan converter lays down
        // slightly more ink at the edges than the glyph mask does. Measured here at about 9.5%; the
        // bound is set at 15% so that ordinary Skia-version drift does not fail the suite, while a
        // route that had genuinely lost or gained a glyph would move it far past that.
        var heavier = Math.Max(blobMass, outlineMass);
        var lighter = Math.Min(blobMass, outlineMass);
        Assert.IsLessThan(
            0.15,
            (heavier - lighter) / (double)heavier,
            $"Total coverage differs too much between the routes: {blobMass} versus {outlineMass}.");

        // And they must put that ink in the same place, to the row.
        Assert.AreEqual(InkRows(viaBlob), InkRows(viaOutlines), "Both routes must ink the same rows.");
    }

    [TestMethod]
    public void BakedOutlinesCoverExactlyTheLayoutsInkBounds()
    {
        using var typesetter = new CoreTypesetter();
        var layout = typesetter.Typeset(new TypesetRequest("Najm", new Style { Size = Size }));

        var path = SkiaTextOutlines.BakePath(layout);

        // The contours come from Skia and the ink box comes from HarfBuzz, independently, from the
        // same font bytes. Their agreeing is a real cross-check on the whole chain — glyph ids,
        // font-unit scaling, and pen positions all have to be right for the two to land on the same
        // rectangle.
        Assert.IsGreaterThan(0, path.Count, "A four-letter word must produce contours.");
        Assert.AreEqual(FillRule.NonZero, path.FillRule, "§7.6 pins nonzero, with the overlap caveat.");

        var bounds = ExtentOf(path);
        Assert.AreEqual(layout.InkBounds.Left, bounds.Left, 0.5f);
        Assert.AreEqual(layout.InkBounds.Top, bounds.Top, 0.5f);
        Assert.AreEqual(layout.InkBounds.Right, bounds.Right, 0.5f);
        Assert.AreEqual(layout.InkBounds.Bottom, bounds.Bottom, 0.5f);
    }

    [TestMethod]
    public void AnEmptyLayoutBakesAnEmptyPathRatherThanFailing()
    {
        using var typesetter = new CoreTypesetter();
        var layout = typesetter.Typeset(new TypesetRequest(string.Empty, new Style { Size = Size }));

        Assert.AreEqual(0, SkiaTextOutlines.BakePath(layout).Count);
    }

    [TestMethod]
    public void TheColorOverrideRepaintsWithoutRetypesetting()
    {
        using var typesetter = new CoreTypesetter();
        var layout = typesetter.Typeset(new TypesetRequest("N", new Style { Size = Size }));
        var entries = typesetter.CachedLayoutCount;

        var black = RenderLayout(layout, outlines: false);
        var red = RenderLayout(layout, outlines: false, color: Color.Srgb(1f, 0f, 0f));

        // I.4: the override replaces the layout's paint table at draw time. The pixels change, the
        // layout does not, and no cache entry appears — which is what makes a colour tween free.
        CollectionAssert.AreNotEqual(black, red, "The override must reach the glyphs.");
        Assert.AreEqual(entries, typesetter.CachedLayoutCount);

        // Red over white: the red channel stays high where the glyph is, and green collapses.
        var (blackMinGreen, _) = Extremes(black, channel: 1);
        var (redMinGreen, _) = Extremes(red, channel: 1);
        var (_, redMaxRed) = Extremes(red, channel: 0);
        Assert.IsLessThan(64, blackMinGreen, "Black text darkens green.");
        Assert.IsLessThan(64, redMinGreen, "Red text darkens green too.");
        Assert.AreEqual(255, redMaxRed, "Red text leaves the red channel saturated.");
    }

    private static byte[] RenderScreen(string text, Action<TextNode>? configure = null)
    {
        var scene = new Scene { VirtualResolution = new Vector2(Width, Height) };
        var layer = scene.Layers.Add(new ScreenLayer { ClearColor = Color.White });
        var node = layer.Root.Add(new TextNode(text) { Size = Size });

        // A screen layer's coordinates are virtual coordinates outright, so this puts the node's
        // origin — its first baseline, under the default anchor — at the frame centre.
        node.Position = new Vector2(Width / 2f, Height / 2f);
        configure?.Invoke(node);
        return Render(scene);
    }

    private static byte[] RenderWorld(string text, Action<TextNode>? configure = null)
    {
        var scene = new Scene { VirtualResolution = new Vector2(Width, Height) };
        var layer = scene.Layers.Add(new WorldLayer2D { ClearColor = Color.White });
        var node = layer.Root.Add(new TextNode(text) { Size = Size });

        // The default camera sits at the world origin with zoom 1, mapping world (x, y) to virtual
        // (W/2 + x, H/2 − y). A node left at the world origin therefore lands on the very same
        // device point the screen-layer node above does.
        configure?.Invoke(node);
        return Render(scene);
    }

    private static byte[] Render(Scene scene)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var typesetter = new CoreTypesetter();
        scene.Load(new SceneEnvironment(provider, typesetter: typesetter));
        try
        {
            using var target = provider.CreateTarget(new SurfaceSpec(Width, Height));
            scene.Render(target);
            return ReadRgba(target);
        }
        finally
        {
            scene.Unload();
        }
    }

    /// <summary>Draws one layout straight onto a target, on whichever of the two routes is asked for.</summary>
    private static byte[] RenderLayout(ITextLayout layout, bool outlines, Color? color = null)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = new SkiaRenderTarget(
            CreateSurface(),
            new SurfaceSpec(Width, Height),
            outlines ? RenderCaps.SkiaSurface | RenderCaps.VectorTarget : RenderCaps.SkiaSurface);
        var context = target.GetContext();
        context.Clear(Color.White);
        context.PushTransform(Matrix3x2.CreateTranslation(10f, Height * 0.7f));
        context.DrawText(layout, color);
        context.PopTransform();
        return ReadRgba(target);
    }

    private static SkiaSharp.SKSurface CreateSurface() =>
        SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(
            Width,
            Height,
            SkiaSharp.SKColorType.Rgba8888,
            SkiaSharp.SKAlphaType.Premul))
        ?? throw new InvalidOperationException("Skia failed to create the comparison surface.");

    private static byte[] ReadRgba(IRenderTarget target)
    {
        using var snapshot = target.Snapshot();
        var pixels = new byte[Width * Height * 4];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    /// <summary>Finds the first and last device rows carrying any non-background pixel.</summary>
    private static (int Top, int Bottom)? InkRows(byte[] pixels)
    {
        var top = -1;
        var bottom = -1;
        for (var row = 0; row < Height; row++)
        {
            for (var column = 0; column < Width; column++)
            {
                var index = ((row * Width) + column) * 4;
                if (pixels[index] == 255 && pixels[index + 1] == 255 && pixels[index + 2] == 255)
                {
                    continue;
                }

                top = top < 0 ? row : top;
                bottom = row;
                break;
            }
        }

        return top < 0 ? null : (top, bottom);
    }

    private static (byte Min, byte Max) Extremes(byte[] pixels, int channel)
    {
        byte min = 255;
        byte max = 0;
        for (var index = channel; index < pixels.Length; index += 4)
        {
            min = Math.Min(min, pixels[index]);
            max = Math.Max(max, pixels[index]);
        }

        return (min, max);
    }

    /// <summary>Returns the axis-aligned extent of every point a portable path visits.</summary>
    private static Rect ExtentOf(PathBuilder path)
    {
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        foreach (var command in path.Commands)
        {
            var count = command.Verb switch
            {
                PathVerb.Move or PathVerb.Line => 1,
                PathVerb.Quadratic => 2,
                PathVerb.Cubic => 3,
                _ => 0,
            };
            for (var index = 0; index < count; index++)
            {
                var point = index switch
                {
                    0 => command.Point1,
                    1 => command.Point2,
                    _ => command.Point3,
                };
                left = MathF.Min(left, point.X);
                top = MathF.Min(top, point.Y);
                right = MathF.Max(right, point.X);
                bottom = MathF.Max(bottom, point.Y);
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
