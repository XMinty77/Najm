namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The offline loop tells the scene what its target can actually do, which is the gap that made the
/// attach-time interop check unwritable.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OfflineRenderer"/> built its <see cref="SceneEnvironment"/> without capabilities, so
/// <see cref="SceneEnvironment.Caps"/> was <see cref="RenderCaps.None"/> on every offline run —
/// including a run through a GPU-backed provider, which is the one configuration where content
/// needing <see cref="RenderCaps.GpuBacked"/> is correct. A scene could only find out by reading the
/// draw context inside <c>Render</c>, a frame after the point where it could have refused.
/// </para>
/// <para>
/// These tests pin the forwarding in both directions: what the provider promises reaches the scene,
/// and a provider that promises nothing still says nothing.
/// </para>
/// </remarks>
[TestClass]
public sealed class CapabilityForwardingTests
{
    [TestMethod]
    public void ASequenceRunLoadsTheSceneWithTheProvidersCapabilities()
    {
        var scene = new CapsCapturingScene();
        using var surfaces = new StubSurfaceProvider
        {
            Caps = RenderCaps.SkiaSurface | RenderCaps.GpuBacked,
        };

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = new RecordingFrameSink(), Frames = 1L });

        Assert.AreEqual(RenderCaps.SkiaSurface | RenderCaps.GpuBacked, scene.CapturedCaps);
    }

    [TestMethod]
    public void AStillLoadsTheSceneWithTheProvidersCapabilities()
    {
        var scene = new CapsCapturingScene();
        using var surfaces = new StubSurfaceProvider
        {
            Caps = RenderCaps.SkiaSurface | RenderCaps.GpuBacked,
        };

        OfflineRenderer.RenderStill(scene, surfaces, new RecordingFrameSink(), at: 0d);

        Assert.AreEqual(RenderCaps.SkiaSurface | RenderCaps.GpuBacked, scene.CapturedCaps);
    }

    [TestMethod]
    public void TheAnswerIsAvailableAtLoad_WhichIsWhatMakesItAContractRatherThanADiagnostic()
    {
        // The point of the forwarding is the moment it happens. A scene that refuses in OnLoad has
        // rendered nothing and cost nothing; the same refusal from Render arrives after the loop has
        // loaded, ticked, and begun a frame, and then arrives again on every frame after that.
        var scene = new GpuRequiringScene();
        using var surfaces = new StubSurfaceProvider();
        var sink = new RecordingFrameSink();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => OfflineRenderer.Render(
                scene,
                surfaces,
                new OfflineOptions { Sink = sink, Frames = 8L }));

        StringAssert.Contains(refusal.Message, "GpuBacked");
        Assert.AreEqual(0, sink.BeginCount, "The stream must never have opened.");
        Assert.AreEqual(0, surfaces.TargetsCreated, "Nor a surface been allocated for it.");
    }

    [TestMethod]
    public void AProviderThatPromisesNothingStillPromisesNothing()
    {
        // The forwarding must not invent a capability. A backend-free provider reports None and the
        // environment says None, exactly as it did before this seam existed.
        var scene = new CapsCapturingScene();
        using var surfaces = new StubSurfaceProvider();

        OfflineRenderer.RenderStill(scene, surfaces, new RecordingFrameSink(), at: 0d);

        Assert.AreEqual(RenderCaps.None, scene.CapturedCaps);
    }

    private sealed class CapsCapturingScene : Scene
    {
        internal RenderCaps CapturedCaps { get; private set; } = (RenderCaps)(-1);

        protected override void OnLoad() => CapturedCaps = Env.Caps;
    }

    /// <summary>A scene that does what NAJM-SKIA I.7 asks for: decides at attach, not at draw.</summary>
    private sealed class GpuRequiringScene : Scene
    {
        protected override void OnLoad()
        {
            if (!Env.Caps.HasFlag(RenderCaps.GpuBacked))
            {
                throw new InvalidOperationException(
                    $"This scene samples an author-owned GL texture and needs {nameof(RenderCaps.GpuBacked)}.");
            }
        }
    }
}
