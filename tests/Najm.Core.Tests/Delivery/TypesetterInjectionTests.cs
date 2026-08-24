using Najm.Core.Tests.Delivery;
using Najm.Core.Text;

namespace Najm.Core.Tests.Delivery;

/// <summary>
/// The offline loop hands the scene the typesetter it was given, which is the whole of the gap this
/// slice closed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OfflineRenderer"/> built its own <see cref="SceneEnvironment"/> around the surface
/// provider and nothing else, and neither it nor the backend conveniences over it exposed a way in.
/// An offline run has no host, so nothing else would ever have supplied one: every text node in
/// every exported figure failed at attach, correctly and uselessly. These tests pin the seam so it
/// cannot close again.
/// </para>
/// <para>
/// The default stays <see cref="NullTypesetter"/>, which is the other half of the contract: a run
/// that draws no text pulls in no text assembly, and an omission still reports itself by name.
/// </para>
/// </remarks>
[TestClass]
public sealed class TypesetterInjectionTests
{
    [TestMethod]
    public void ASequenceRunHandsTheSceneTheTypesetterItWasGiven()
    {
        var typesetter = new StubTypesetter();
        var scene = new EnvironmentCapturingScene();
        using var surfaces = new StubSurfaceProvider();

        OfflineRenderer.Render(
            scene,
            surfaces,
            new OfflineOptions { Sink = new RecordingFrameSink(), Frames = 1L, Typesetter = typesetter });

        Assert.AreSame(typesetter, scene.CapturedTypesetter);
    }

    [TestMethod]
    public void AStillHandsTheSceneTheTypesetterItWasGiven()
    {
        var typesetter = new StubTypesetter();
        var scene = new EnvironmentCapturingScene();
        using var surfaces = new StubSurfaceProvider();

        OfflineRenderer.RenderStill(scene, surfaces, new RecordingFrameSink(), at: 0d, typesetter: typesetter);

        Assert.AreSame(typesetter, scene.CapturedTypesetter);
    }

    [TestMethod]
    public void OmittingItLeavesTheFailLoudNullObjectInPlace()
    {
        var sequence = new EnvironmentCapturingScene();
        var still = new EnvironmentCapturingScene();
        using var surfaces = new StubSurfaceProvider();

        OfflineRenderer.Render(
            sequence,
            surfaces,
            new OfflineOptions { Sink = new RecordingFrameSink(), Frames = 1L });
        OfflineRenderer.RenderStill(still, surfaces, new RecordingFrameSink(), at: 0d);

        // Not null, and not a silent no-op: the capability that was omitted is the one that reports
        // the omission, by name, at the first call.
        Assert.AreSame(NullTypesetter.Instance, sequence.CapturedTypesetter);
        Assert.AreSame(NullTypesetter.Instance, still.CapturedTypesetter);
    }

    private sealed class EnvironmentCapturingScene : Scene
    {
        internal ITypesetter? CapturedTypesetter { get; private set; }

        protected override void OnLoad() => CapturedTypesetter = Env.Typesetter;
    }

    private sealed class StubTypesetter : ITypesetter
    {
        public void RegisterFamily(FontFamily family)
        {
        }

        public void SetDefaultFamilies(string textFamily, string mathFamily)
        {
        }

        public FontMetrics Metrics(FontFace face, float size) => default;

        public ITextLayout Typeset(in TypesetRequest request) =>
            throw new NotSupportedException("This stub exists to be injected, not to typeset.");
    }
}
