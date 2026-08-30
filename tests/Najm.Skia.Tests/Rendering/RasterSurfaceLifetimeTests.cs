using System.Numerics;
using Najm.Core;
using Najm.Utils;

namespace Najm.Skia.Tests.Rendering;

[TestClass]
public sealed class RasterSurfaceLifetimeTests
{
    [TestMethod]
    public void Snapshot_RemainsUsableAfterSourceTargetIsDisposed()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        var source = provider.CreateTarget(new SurfaceSpec(1, 1));
        source.GetContext().Clear(Color.Srgb(1f, 0f, 0f));
        using var snapshot = source.Snapshot();

        source.Dispose();

        var sourcePixels = new byte[4];
        snapshot.CopyPixels(sourcePixels, PixelFormat.Rgba8888);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, sourcePixels);

        using var destination = provider.CreateTarget(new SurfaceSpec(1, 1));
        destination.GetContext().DrawImage(
            snapshot,
            Matrix3x2.Identity,
            ImageSampling.Nearest);
        using var destinationSnapshot = destination.Snapshot();
        var destinationPixels = new byte[4];
        destinationSnapshot.CopyPixels(destinationPixels, PixelFormat.Rgba8888);
        CollectionAssert.AreEqual(sourcePixels, destinationPixels);
    }

    [TestMethod]
    public void Caps_AreTheTargetsCaps_AndSurviveDisposal()
    {
        // The provider's answer is what an attaching node reads through Env.Surfaces, and the
        // target's is what a drawable reads mid-render. They have to be the same statement or the
        // attach-time check is checking something else.
        var provider = new RasterSkiaSurfaceProvider();
        using (var target = provider.CreateTarget(new SurfaceSpec(1, 1)))
        {
            Assert.AreEqual(RenderCaps.SkiaSurface, provider.Caps);
            Assert.AreEqual(provider.Caps, target.GetContext().Caps);
        }

        provider.Dispose();

        // Capabilities describe the backend, not whether the provider is still open. A teardown path
        // that asks what it was rendering through must not be the thing that throws.
        Assert.AreEqual(RenderCaps.SkiaSurface, provider.Caps);
    }

    [TestMethod]
    public void DisposedProvider_RejectsNewTargets()
    {
        var provider = new RasterSkiaSurfaceProvider();
        var spec = new SurfaceSpec(1, 1);
        provider.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => provider.CreateTarget(spec));
    }

    [TestMethod]
    public void DisposedTarget_RejectsContextSnapshotAndBorrowedContextUse()
    {
        using var provider = new RasterSkiaSurfaceProvider();
        var target = provider.CreateTarget(new SurfaceSpec(1, 1));
        var context = target.GetContext();
        var path = new PathBuilder(initialCapacity: 4)
            .MoveTo(0f, 0f)
            .LineTo(1f, 0f)
            .LineTo(0f, 1f)
            .Close();
        context.PushTransform(Matrix3x2.CreateTranslation(1f, 1f));
        context.PushClip(new Rect(0f, 0f, 1f, 1f));
        context.PushOpacity(0.5f);
        context.DrawPath(path, Paint.Fill(Color.Srgb(1f, 0f, 0f)));

        target.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => target.GetContext());
        Assert.ThrowsExactly<ObjectDisposedException>(() => target.Snapshot());
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.DrawPath(path, default));
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.Clear(default));
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.PopOpacity());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = context.Scale);
        target.Dispose();
    }
}
