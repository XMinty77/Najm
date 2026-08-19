using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

/// <summary>
/// Pixel proof for the one thing a per-segment ramped run can get visibly wrong: what happens where
/// two segments meet.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDrawContext2D.DrawGradientPolyline"/> and
/// <see cref="IDrawContext2D.DrawGradientSpline"/> emit one stroked path per segment, so a
/// translucent run composites against itself at every join unless the segments are made to abut.
/// They are: every interior segment end is <see cref="LineCap.Butt"/>. The residue, measured here
/// rather than asserted, is in two places — the antialiasing seam wherever a join does not fall on a
/// pixel boundary, and the two joins either side of an open run's ends, whose segments carry the
/// caller's cap because a <see cref="Paint"/> caps both ends of the path it is on.
/// </para>
/// <para>
/// Every expectation is derived from source-over arithmetic in the comment above it.
/// </para>
/// </remarks>
[TestClass]
public sealed class GradientRunGoldenTests
{
    /// <summary>0.2 · 255 = 51 exactly, so the composite is derivable in eight bits.</summary>
    private const float Fifth = 0.2f;

    /// <summary>One source-over pass of <see cref="Fifth"/> red over opaque black.</summary>
    private const int OnePass = 51;

    private static readonly Color TranslucentRed = Color.Srgb(1f, 0f, 0f, Fifth);

    /// <summary>An eight-pixel row with a vertex every two pixels: four segments, three joins.</summary>
    private static readonly Vector2[] FiveAcrossEight =
    [
        new(0f, 0.5f), new(2f, 0.5f), new(4f, 0.5f), new(6f, 0.5f), new(8f, 0.5f),
    ];

    [TestMethod]
    public void AButtCappedRunIsPaintedExactlyOnceEndToEnd()
    {
        // 8×1, opaque black backdrop. A straight run along the middle of the single pixel row at
        // stroke width 1, so the stroke covers y ∈ [0,1] — the whole row — and every join lands on
        // an even pixel boundary. With the default butt cap no segment overlaps another and no
        // segment end protrudes past the run, so every pixel is one source-over pass:
        //   out = 0.2·255 + (1 − 0.2)·0 = 51 = 0x33.
        // Any double blend anywhere would raise a pixel to at most 1 − 0.8² = 0.36 ⇒ 92, and this
        // frame has no pixel above 51.
        var pixels = Render(8, context => context.DrawGradientPolyline(
            FiveAcrossEight,
            [],
            [],
            Paint.Stroke(TranslucentRed, 1f)));

        Assert.AreEqual(string.Concat(Enumerable.Repeat("330000ff", 8)), Hex(pixels));
    }

    [TestMethod]
    public void AnInteriorJoinIsExactAndOnlyTheRunsOwnEndCapsOverlapAnything()
    {
        // The same run with the caller asking for round caps. A Paint caps both ends of the path it
        // is on, so the two terminal segments — which must carry the caller's cap at the run's outer
        // ends — carry it at their interior end too, and a half-disc of radius 0.5 protrudes into
        // the neighbour there. Interior segments have no such excuse and abut exactly.
        //
        //   x ∈ [0,2)  segment 0 body                              ⇒ 51
        //   x ∈ [2,3)  segment 1 body + segment 0's forward cap    ⇒ above 51
        //   x ∈ [3,4)  segment 1 body                              ⇒ 51
        //   x ∈ [4,5)  segment 2 body, joined butt-to-butt at x=4  ⇒ 51   ← the interior join
        //   x ∈ [5,6)  segment 2 body + segment 3's backward cap   ⇒ above 51
        //   x ∈ [6,8)  segment 3 body                              ⇒ 51
        //
        // The cap is a half-disc of area π·0.5²/2 ≈ 0.393, so those two pixels land near
        // 51 + (1 − 0.2)·0.2·0.393·255 ≈ 67, and cannot exceed the two-full-passes bound of 92.
        var pixels = Render(8, context => context.DrawGradientPolyline(
            FiveAcrossEight,
            [],
            [],
            Paint.Stroke(TranslucentRed, 1f, cap: LineCap.Round)));

        int[] singlePass = [0, 1, 3, 4, 6, 7];
        foreach (var pixel in singlePass)
        {
            Assert.AreEqual(
                OnePass,
                pixels[pixel * 4],
                $"Pixel {pixel} is painted once; the join at x = 4 in particular must be exact.");
        }

        foreach (var pixel in new[] { 2, 5 })
        {
            Assert.IsGreaterThan(
                OnePass,
                (int)pixels[pixel * 4],
                $"Pixel {pixel} carries a terminal segment's cap over its neighbour.");
            Assert.IsLessThanOrEqualTo(
                92,
                (int)pixels[pixel * 4],
                "And at most two passes' worth, which one cap can never exceed.");
        }
    }

    [TestMethod]
    public void TheWorkaroundThisReplacesBeadedEveryJoin_WhichIsWhyInteriorEndsAbut()
    {
        // What both samples wrote by hand: one short path per segment, each with the round cap the
        // trail wanted. Every join then carries two half-discs — the outgoing segment's end cap and
        // the incoming segment's start cap — so every join beads, not just the two next to the ends.
        var handRolled = Render(8, context =>
        {
            var paint = Paint.Stroke(TranslucentRed, 1f, cap: LineCap.Round);
            for (var index = 0; index < FiveAcrossEight.Length - 1; index++)
            {
                context.DrawPath(
                    new PathBuilder()
                        .MoveTo(FiveAcrossEight[index].X, FiveAcrossEight[index].Y)
                        .LineTo(FiveAcrossEight[index + 1].X, FiveAcrossEight[index + 1].Y),
                    paint);
            }
        });

        // Every pixel adjacent to a join — 1 and 2 at x=2, 3 and 4 at x=4, 5 and 6 at x=6 — is
        // painted twice. That is six of the eight pixels, against two for the convenience.
        foreach (var pixel in new[] { 1, 2, 3, 4, 5, 6 })
        {
            Assert.IsGreaterThan(
                OnePass,
                (int)handRolled[pixel * 4],
                $"Pixel {pixel} sits beside a join and the hand-rolled loop beads it.");
        }

        // The convenience leaves the two pixels either side of the middle join clean, which is the
        // whole difference between the two renders.
        var convenience = Render(8, context => context.DrawGradientPolyline(
            FiveAcrossEight,
            [],
            [],
            Paint.Stroke(TranslucentRed, 1f, cap: LineCap.Round)));

        Assert.AreEqual(OnePass, convenience[3 * 4]);
        Assert.AreEqual(OnePass, convenience[4 * 4]);
    }

    [TestMethod]
    public void ARampedRunReachesTheFrameWithTheColorEachSegmentResolvedTo()
    {
        // 4×1, opaque black backdrop, one row again. Three vertices at α = 0.2, 0.2, 0.6, all red:
        //   segment 0 midpoint α = (0.2 + 0.2)/2 = 0.2 ⇒ 51 = 0x33 over pixels 0 and 1
        //   segment 1 midpoint α = (0.2 + 0.6)/2 = 0.4 ⇒ 102 = 0x66 over pixels 2 and 3
        // Equal hues make the premultiplied mean the plain mean, so the red channel stays 255 and
        // the whole difference is the alpha the ramp resolved to. Both segments are terminal here,
        // but the default butt cap means neither reaches into the other.
        var pixels = Render(4, context => context.DrawGradientPolyline(
            [new Vector2(0f, 0.5f), new Vector2(2f, 0.5f), new Vector2(4f, 0.5f)],
            [TranslucentRed, TranslucentRed, Color.Srgb(1f, 0f, 0f, 0.6f)],
            [],
            Paint.Stroke(Color.White, 1f)));

        Assert.AreEqual("330000ff330000ff660000ff660000ff", Hex(pixels));
    }

    [TestMethod]
    public void TheOnlyResidualAtAnInteriorJoinIsTheAntialiasingSeam()
    {
        // The honest bound on what per-segment emission still costs where a join does not fall on a
        // pixel boundary. A collinear 45° run through (16,16) on a 32×32 surface, butt-capped at
        // width 4 and α = 0.2, against the identical geometry as one continuous stroked path — the
        // ideal this lowers to when §7.3's batch tier arrives. Collinear, so the single path's join
        // adds no geometry and the two renders describe the same region exactly.
        //
        // Two abutting coverage-antialiased edges split a pixel's coverage c and 1 − c between them
        // and composite separately, so the seam's alpha falls short by α²·c(1 − c) ≤ α²/4 = 0.01 —
        // one percent of full, 2.6 of 255. Two composites round to eight bits instead of one, and
        // Skia's analytic AA approximates area rather than integrating it, so the observed worst is
        // a few units above that arithmetic floor rather than at it. Eight is the line between "a
        // seam" and "the segments do not actually abut": a gap or an overlap of half a stroke width
        // would cost tens of units, not single digits.
        Vector2[] points = [new(0f, 0f), new(16f, 16f), new(32f, 32f)];
        var paint = Paint.Stroke(TranslucentRed, 4f);

        var segmented = Render(32, 32, context => context.DrawGradientPolyline(points, [], [], paint));
        var single = Render(32, 32, context => context.DrawPath(
            new PathBuilder().MoveTo(0f, 0f).LineTo(16f, 16f).LineTo(32f, 32f),
            paint));

        var worst = 0;
        var differing = 0;
        for (var index = 0; index < single.Length; index++)
        {
            var delta = Math.Abs(single[index] - segmented[index]);
            if (delta != 0)
            {
                differing++;
                worst = Math.Max(worst, delta);
            }
        }

        Assert.IsLessThanOrEqualTo(
            8,
            worst,
            $"The seam must stay within the coverage-splitting bound; it reached {worst} of 255 "
                + $"across {differing} bytes.");

        // The stroke is 4 wide across a 45° join, so the seam is a transverse hairline about
        // 4·√2 ≈ 5.7 pixels long, plus the antialiased fringe either side of it. A hundred-odd bytes
        // is that hairline; a few thousand would mean the two renders disagree about where the whole
        // line is.
        Assert.IsLessThan(200, differing, "The difference must be a hairline, not the whole stroke.");
        Assert.IsGreaterThan(0, differing, "And it must be measured against a render that drew something.");
    }

    private static byte[] Render(int width, Action<IDrawContext2D> draw) => Render(width, 1, draw);

    /// <summary>Renders one draw over an opaque black backdrop, so alpha reads as a plain fraction.</summary>
    private static byte[] Render(int width, int height, Action<IDrawContext2D> draw)
    {
        using var provider = new RasterSkiaSurfaceProvider();
        using var target = provider.CreateTarget(new SurfaceSpec(width, height));
        var context = target.GetContext();
        context.Clear(Color.Srgb(0f, 0f, 0f));
        draw(context);

        using var snapshot = target.Snapshot();
        var pixels = new byte[checked(width * height * 4)];
        snapshot.CopyPixels(pixels, PixelFormat.Rgba8888);
        return pixels;
    }

    private static string Hex(byte[] pixels) => Convert.ToHexString(pixels).ToLowerInvariant();
}
