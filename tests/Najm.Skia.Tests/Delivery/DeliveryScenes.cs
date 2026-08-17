using System.Numerics;
using System.Security.Cryptography;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// An 8×4 scene with one red pixel that walks across the top row, one column per tick.
/// </summary>
/// <remarks>
/// The position is a pure function of the tick's frame index — <c>frame mod 8</c> — so the frame at
/// any time is derivable rather than captured, and two runs of two fresh instances must agree
/// exactly. <c>OnStart</c> parks the pixel in the last column, which is a state no ticked frame ever
/// shows: it is visible only if a zero-tick export wrongly ran the start hook.
/// </remarks>
internal sealed class WalkingPixelScene : Scene
{
    internal WalkingPixelScene()
    {
        VirtualResolution = new Vector2(8f, 4f);
        var layer = Layers.Add(new ScreenLayer { ClearColor = DeliveryColors.OpaqueBlack });
        Walker = layer.Root.Add(new WalkingPixel());
    }

    internal WalkingPixel Walker { get; }

    internal int StartCount { get; private set; }

    protected override void OnStart()
    {
        StartCount++;
        Walker.Position = new Vector2(7f, 0f);
    }
}

/// <summary>A one-unit red square that steps one column per tick and wraps every eight.</summary>
internal sealed class WalkingPixel : Drawable
{
    private static readonly Rect UnitSquare = new(0f, 0f, 1f, 1f);

    private readonly PathBuilder path = new PathBuilder(initialCapacity: 5)
        .MoveTo(0f, 0f)
        .LineTo(1f, 0f)
        .LineTo(1f, 1f)
        .LineTo(0f, 1f)
        .Close();

    private readonly Paint paint = Paint.Fill(DeliveryColors.OpaqueRed, isAntialias: false);

    public override Rect GeometryBounds => UnitSquare;

    internal long UpdateCount { get; private set; }

    public override void Render(IDrawContext2D context) => context.DrawPath(path, paint);

    protected override void Update(in TickContext tick)
    {
        UpdateCount++;
        Position = new Vector2((float)(tick.Time.Frame % 8L), 0f);
    }
}

/// <summary>A scene sized for an encoder test: an even 320×240 with a moving band.</summary>
internal sealed class EncoderProbeScene : Scene
{
    private readonly MovingBand band;

    internal EncoderProbeScene()
    {
        VirtualResolution = new Vector2(320f, 240f);
        var layer = Layers.Add(new ScreenLayer { ClearColor = DeliveryColors.OpaqueBlack });
        band = layer.Root.Add(new MovingBand());
    }

    internal long UpdateCount => band.UpdateCount;

    private sealed class MovingBand : Drawable
    {
        private static readonly Rect Band = new(0f, 0f, 32f, 240f);

        private readonly PathBuilder path = new PathBuilder(initialCapacity: 5)
            .MoveTo(0f, 0f)
            .LineTo(32f, 0f)
            .LineTo(32f, 240f)
            .LineTo(0f, 240f)
            .Close();

        private readonly Paint paint = Paint.Fill(DeliveryColors.OpaqueRed, isAntialias: false);

        public override Rect GeometryBounds => Band;

        internal long UpdateCount { get; private set; }

        public override void Render(IDrawContext2D context) => context.DrawPath(path, paint);

        protected override void Update(in TickContext tick)
        {
            UpdateCount++;
            Position = new Vector2((tick.Time.Frame % 9L) * 32f, 0f);
        }
    }
}

/// <summary>The colors the delivery fixtures paint with.</summary>
internal static class DeliveryColors
{
    internal static Color OpaqueBlack { get; } = Color.Srgb(0f, 0f, 0f);

    internal static Color OpaqueRed { get; } = Color.Srgb(1f, 0f, 0f);
}

/// <summary>Hashes each delivered frame instead of writing it anywhere.</summary>
/// <remarks>
/// Determinism is a property of the bytes, so the cheapest sink that can prove it keeps only their
/// digests — no output file, and nothing on a nearly full disk.
/// </remarks>
internal sealed class HashingFrameSink : IFrameSink
{
    internal List<string> Hashes { get; } = [];

    internal FrameStreamInfo Info { get; private set; }

    internal int BeginCount { get; private set; }

    internal int EndCount { get; private set; }

    public void Begin(in FrameStreamInfo info)
    {
        BeginCount++;
        Info = info;
    }

    public void Submit(long frame, PixelFrameLease pixels)
    {
        using (pixels)
        {
            Hashes.Add(Convert.ToHexString(SHA256.HashData(pixels.Pixels)));
        }
    }

    public void End() => EndCount++;
}
