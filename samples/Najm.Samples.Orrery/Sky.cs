using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Samples.Orrery;

/// <summary>
/// The warm atmosphere the sun casts across the whole frame. It lives in virtual space rather
/// than in the world, so the camera's slow breath does not drag it around.
/// </summary>
internal sealed class HazeNode : Drawable
{
    private readonly PathBuilder frame;
    private readonly Vector2 sun;
    private readonly Vector2 size;

    public HazeNode(Vector2 virtualResolution)
    {
        size = virtualResolution;
        sun = Design.SunInFrame;
        frame = new PathBuilder(initialCapacity: 5)
            .MoveTo(0f, 0f)
            .LineTo(size.X, 0f)
            .LineTo(size.X, size.Y)
            .LineTo(0f, size.Y)
            .Close();
    }

    public override Rect VisualBounds => new(0f, 0f, size.X, size.Y);

    public override void Render(IDrawContext2D context)
    {
        context.DrawPath(
            frame,
            Paint.Fill(
                Shapes.Glow(Design.Haze, sun, 1350f, 0.115f),
                blendMode: BlendMode.Plus));
        context.DrawPath(
            frame,
            Paint.Fill(
                Shapes.Glow(Design.Haze, sun, 520f, 0.075f),
                blendMode: BlendMode.Plus));
    }
}

/// <summary>
/// Stars. The still ones are baked into four paths at load and never rebuilt; a handful of
/// twinklers are drawn individually with a slow sinusoidal alpha whose period divides the loop.
/// </summary>
internal sealed class StarfieldNode : Drawable
{
    private const int StarCount = 320;
    private const int Buckets = 4;
    private const int TwinklerCount = 26;

    private readonly PathBuilder[] baked = new PathBuilder[Buckets];
    private readonly PathBuilder scratch = new(initialCapacity: 6);
    private readonly Vector2[] twinklers = new Vector2[TwinklerCount];
    private readonly float[] twinklerSizes = new float[TwinklerCount];
    private readonly float[] twinklerAlphas = new float[TwinklerCount];
    private readonly float[] twinklerPhases = new float[TwinklerCount];
    private readonly int[] twinklerRates = new int[TwinklerCount];
    private readonly Vector2 size;

    private float loopPhase;

    public StarfieldNode(Vector2 virtualResolution)
    {
        size = virtualResolution;
        var random = new Random(1618);

        for (var i = 0; i < Buckets; i++)
        {
            baked[i] = new PathBuilder(initialCapacity: 6 * StarCount);
        }

        for (var i = 0; i < StarCount; i++)
        {
            var point = SampleSky(random);
            var bucket = random.Next(Buckets);
            baked[bucket].AddCircle(point.X, point.Y, 0.45f + (0.85f * (float)random.NextDouble()));
        }

        for (var i = 0; i < TwinklerCount; i++)
        {
            twinklers[i] = SampleSky(random);
            twinklerSizes[i] = 0.9f + (1.0f * (float)random.NextDouble());
            twinklerAlphas[i] = 0.28f + (0.36f * (float)random.NextDouble());
            twinklerPhases[i] = (float)random.NextDouble();
            twinklerRates[i] = 1 + random.Next(3);
        }
    }

    public override Rect VisualBounds => new(0f, 0f, size.X, size.Y);

    public void Advance(double phase) => loopPhase = (float)phase;

    public override void Render(IDrawContext2D context)
    {
        for (var i = 0; i < Buckets; i++)
        {
            var alpha = 0.10f + (0.13f * i);
            context.DrawPath(baked[i], Paint.Fill(Design.Star.WithAlpha(alpha)));
        }

        for (var i = 0; i < TwinklerCount; i++)
        {
            var cycle = MathF.Sin(MathF.Tau * ((twinklerRates[i] * loopPhase) + twinklerPhases[i]));
            var alpha = twinklerAlphas[i] * (0.35f + (0.65f * (0.5f + (0.5f * cycle))));

            scratch.Reset();
            scratch.AddCircle(twinklers[i].X, twinklers[i].Y, twinklerSizes[i]);
            context.DrawPath(
                scratch,
                Paint.Fill(Design.Star.WithAlpha(alpha), blendMode: BlendMode.Plus));
        }
    }

    /// <summary>
    /// Picks a point in the frame, thinning the sky near the sun so the glow is not fighting a
    /// crowd of pinpricks it would wash out anyway.
    /// </summary>
    private Vector2 SampleSky(Random random)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var point = new Vector2(
                (float)random.NextDouble() * size.X,
                (float)random.NextDouble() * size.Y);
            var distance = Vector2.Distance(point, Design.SunInFrame);
            if (distance > 320f || random.NextDouble() < distance / 320d)
            {
                return point;
            }
        }

        return new Vector2((float)random.NextDouble() * size.X, (float)random.NextDouble() * size.Y);
    }
}

/// <summary>A plain corner darkening over the assembled frame. It is the last thing drawn.</summary>
internal sealed class VignetteNode : Drawable
{
    private readonly PathBuilder frame;
    private readonly Paint paint;
    private readonly Vector2 size;

    public VignetteNode(Vector2 virtualResolution)
    {
        size = virtualResolution;
        frame = new PathBuilder(initialCapacity: 5)
            .MoveTo(0f, 0f)
            .LineTo(size.X, 0f)
            .LineTo(size.X, size.Y)
            .LineTo(0f, size.Y)
            .Close();
        paint = Paint.Fill(
            Shapes.Falloff(
                Design.Space.Shade(0.30f, 1f),
                new Vector2(size.X * 0.46f, size.Y * 0.52f),
                MathF.Max(size.X, size.Y) * 0.82f,
                0.62f));
    }

    public override Rect VisualBounds => new(0f, 0f, size.X, size.Y);

    public override void Render(IDrawContext2D context) => context.DrawPath(frame, paint);
}
