using System.Numerics;
using Najm.Core;
using Najm.Lib;
using Najm.Utils;
using CoreTypesetter = Najm.Text.Typesetter;

namespace Najm.Skia.Tests.Delivery;

/// <summary>
/// The end of the injection thread: a scene with a caption on it actually exports, through the
/// convenience an author actually calls.
/// </summary>
/// <remarks>
/// This is the acceptance test for the gap the slice closed, and it is deliberately written at the
/// outermost layer rather than against <see cref="OfflineRenderer"/> directly. The option existing
/// on <see cref="OfflineOptions"/> is not the same thing as an author being able to reach it:
/// <see cref="SkiaOffline"/> and <see cref="SkiaExport"/> are what a sample calls, and if the
/// typesetter did not survive the trip through them the option would be a fix nobody could apply.
/// </remarks>
[TestClass]
public sealed class TextExportTests
{
    [TestMethod]
    public void ASceneWithTextExportsAPngThroughSkiaExport()
    {
        using var scratch = new ScratchDirectory();
        using var typesetter = new CoreTypesetter();
        var path = scratch.File("caption.png");

        SkiaExport.Png(() => new CaptionScene(), path, at: 0d, typesetter: typesetter);

        Assert.IsTrue(File.Exists(path));
        Assert.IsGreaterThan(0L, new FileInfo(path).Length);
    }

    [TestMethod]
    public void ASceneWithTextRunsAsASequenceThroughSkiaOffline()
    {
        var sink = new CountingSink();
        using var typesetter = new CoreTypesetter();

        var frames = SkiaOffline.Render(
            () => new CaptionScene(),
            new OfflineOptions { Sink = sink, Frames = 3L, Typesetter = typesetter });

        Assert.AreEqual(3L, frames);
        Assert.AreEqual(3, sink.Submitted);
        Assert.IsGreaterThan(0, sink.NonWhitePixels, "The caption must have painted something.");
    }

    [TestMethod]
    public void WithoutATypesetterTheExportFailsAndNamesTheOptionToSet()
    {
        using var scratch = new ScratchDirectory();

        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => SkiaExport.Png(() => new CaptionScene(), scratch.File("nope.png"), at: 0d));

        // The failure an author hits before they know the option exists has to be the thing that
        // tells them about it. Both names, in the message, at the moment the text node gives up.
        Assert.Contains("Najm.Text", error.Message);
        Assert.Contains("OfflineOptions.Typesetter", error.Message);
    }

    private sealed class CaptionScene : Scene
    {
        public CaptionScene() => VirtualResolution = new Vector2(240f, 80f);

        protected override void OnLoad()
        {
            var layer = Layers.Add(new ScreenLayer { ClearColor = Color.White });
            layer.Root.Add(new TextNode("Najm")
            {
                Size = 40f,
                Color = Color.Black,
                Position = new Vector2(20f, 55f),
            });
        }
    }

    /// <summary>Counts frames and how much ink each one carried.</summary>
    private sealed class CountingSink : IFrameSink
    {
        internal int Submitted { get; private set; }

        internal int NonWhitePixels { get; private set; }

        public void Begin(in FrameStreamInfo info)
        {
        }

        public void Submit(long frame, PixelFrameLease pixels)
        {
            using (pixels)
            {
                Submitted++;
                var span = pixels.Pixels;
                for (var index = 0; index < span.Length; index += 4)
                {
                    if (span[index] != 255 || span[index + 1] != 255 || span[index + 2] != 255)
                    {
                        NonWhitePixels++;
                    }
                }
            }
        }

        public void End()
        {
        }
    }
}
