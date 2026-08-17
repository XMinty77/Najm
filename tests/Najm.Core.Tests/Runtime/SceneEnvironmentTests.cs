using Najm.Core.Tests.Delivery;

namespace Najm.Core.Tests.Runtime;

/// <summary>
/// The closed capability set and its binding to a scene: what an environment holds, what Core's
/// null objects do in place of the capabilities a host did not supply, how a decorated copy is
/// made, and the window during which <see cref="Scene.Env"/> means anything.
/// </summary>
[TestClass]
public sealed class SceneEnvironmentTests
{
    [TestMethod]
    public void AnEnvironmentBuiltFromJustAProviderFillsTheOtherFourWithNullObjects()
    {
        using var surfaces = new StubSurfaceProvider();

        var env = new SceneEnvironment(surfaces);

        Assert.AreSame(surfaces, env.Surfaces);
        Assert.AreSame(NullAssets.Instance, env.Assets);
        Assert.AreSame(NullTypesetter.Instance, env.Typesetter);
        Assert.AreSame(NullAudioSink.Instance, env.Audio);
        Assert.AreEqual(RenderCaps.None, env.Caps);
    }

    [TestMethod]
    public void AnEnvironmentWithoutASurfaceProviderCannotBeBuiltAndCannotLoadAScene()
    {
        // Surfaces is the one capability with no null object: a scene renders, a render needs the
        // compositor the provider creates, so an environment without one describes a scene that
        // cannot run. The absence is therefore rejected where it is introduced — at construction —
        // and the only way to reach Load without a provider is to pass no environment at all.
        var missing = Assert.ThrowsExactly<ArgumentNullException>(
            () => new SceneEnvironment(surfaces: null!));

        Assert.AreEqual("surfaces", missing.ParamName);

        var scene = new Scene();

        Assert.ThrowsExactly<ArgumentNullException>(() => scene.Load(null!));
        Assert.AreEqual(SceneState.Constructed, scene.State, "A refused load must not move the scene.");

        scene.Load(TestEnvironment.Stub());

        Assert.AreEqual(SceneState.Loaded, scene.State);
    }

    [TestMethod]
    public void EnvIsThePassedEnvironmentWhileLoadedAndIsUnavailableOutsideThatWindow()
    {
        using var surfaces = new StubSurfaceProvider();
        var assets = new StubAssets();
        var typesetter = new StubTypesetter();
        var audio = new StubAudioSink();
        var env = new SceneEnvironment(surfaces, assets, typesetter, audio, RenderCaps.SkiaSurface);
        var scene = new EnvProbeScene();

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = scene.Env, "Not loaded yet.");

        scene.Load(env);

        Assert.AreSame(env, scene.Env);
        Assert.AreSame(surfaces, scene.Env.Surfaces);
        Assert.AreSame(assets, scene.Env.Assets);
        Assert.AreSame(typesetter, scene.Env.Typesetter);
        Assert.AreSame(audio, scene.Env.Audio);
        Assert.AreEqual(RenderCaps.SkiaSurface, scene.Env.Caps);
        Assert.AreSame(env, scene.EnvSeenInOnLoad, "OnLoad must already see the bound environment.");
        Assert.AreEqual(1, surfaces.CompositorsCreated, "Load acquires the compositor from env.Surfaces.");

        scene.Tick(RuntimeTicks.At(0));

        Assert.AreSame(env, scene.Env, "A ticked scene is still loaded.");

        scene.Stop();

        Assert.AreSame(env, scene.Env, "Stop ends the run, not the binding.");

        scene.Unload();

        Assert.AreSame(env, scene.EnvSeenInOnUnload, "OnUnload must still see it — that is where a scene releases.");
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = scene.Env, "Unloaded scenes hold no host state.");
        Assert.IsNull(scene.Compositor);
    }

    [TestMethod]
    public void WithReplacesOnlyTheCapabilitiesItIsGivenAndLeavesTheOriginalAlone()
    {
        using var surfaces = new StubSurfaceProvider();
        var assets = new StubAssets();
        var typesetter = new StubTypesetter();
        var audio = new StubAudioSink();
        var original = new SceneEnvironment(surfaces, assets, typesetter, audio, RenderCaps.GpuBacked);
        var decorated = new StubAudioSink();

        var wrapped = original.With(audio: decorated);

        Assert.AreNotSame(original, wrapped, "A decorated environment is a copy, not a mutation.");
        Assert.AreSame(decorated, wrapped.Audio);
        Assert.AreSame(surfaces, wrapped.Surfaces);
        Assert.AreSame(assets, wrapped.Assets);
        Assert.AreSame(typesetter, wrapped.Typesetter);
        Assert.AreEqual(RenderCaps.GpuBacked, wrapped.Caps, "An omitted Caps keeps its value, it does not reset.");
        Assert.AreSame(audio, original.Audio, "The original must be untouched.");

        // Omitting everything is the identity, which is the property a decorator chain relies on.
        var copy = original.With();

        Assert.AreSame(original.Surfaces, copy.Surfaces);
        Assert.AreSame(original.Assets, copy.Assets);
        Assert.AreSame(original.Typesetter, copy.Typesetter);
        Assert.AreSame(original.Audio, copy.Audio);
        Assert.AreEqual(original.Caps, copy.Caps);

        // RenderCaps.None is a value like any other, so it must be settable rather than read as
        // "not given" — that is the whole reason the parameter is nullable.
        Assert.AreEqual(RenderCaps.None, original.With(caps: RenderCaps.None).Caps);

        using var otherSurfaces = new StubSurfaceProvider();
        var otherAssets = new StubAssets();
        var otherTypesetter = new StubTypesetter();
        var replaced = original.With(otherSurfaces, otherAssets, otherTypesetter, decorated, RenderCaps.VectorTarget);

        Assert.AreSame(otherSurfaces, replaced.Surfaces);
        Assert.AreSame(otherAssets, replaced.Assets);
        Assert.AreSame(otherTypesetter, replaced.Typesetter);
        Assert.AreSame(decorated, replaced.Audio);
        Assert.AreEqual(RenderCaps.VectorTarget, replaced.Caps);
    }

    [TestMethod]
    public void NullTypesetterRefusesEveryCallAndItsMessageIsTheFix()
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(
            () => NullTypesetter.Instance.SetDefaultFamilies("Latin Modern Roman", "Latin Modern Math"));

        Assert.IsTrue(
            failure.Message.Contains("HostOptions.Typesetter", StringComparison.Ordinal),
            $"The message must name the option to set. It said: {failure.Message}");
        Assert.IsTrue(
            failure.Message.Contains("Najm.Text", StringComparison.Ordinal),
            $"The message must name the assembly that supplies a real typesetter. It said: {failure.Message}");

        // The default typesetter of an environment nobody injected one into is this one, so a scene
        // that tries to typeset gets the explanation rather than a blank frame.
        using var surfaces = new StubSurfaceProvider();
        var env = new SceneEnvironment(surfaces);

        Assert.ThrowsExactly<InvalidOperationException>(() => env.Typesetter.SetDefaultFamilies("a", "b"));
    }

    [TestMethod]
    public void NullAudioSinkSwallowsEveryEmissionWithoutComplaint()
    {
        // Silence is a correct configuration — every offline render is one — so the audio null
        // object is the opposite of the typesetter's: it accepts anything, including a clip it was
        // handed no way to play, and reports nothing.
        using var surfaces = new StubSurfaceProvider();
        var env = new SceneEnvironment(surfaces);
        var clip = new StubAudioClip();

        env.Audio.Play(clip, at: 0d);
        env.Audio.Play(clip, at: 1.5d, gain: 0.8f);
        env.Audio.Play(clip, at: double.MaxValue, gain: float.NaN);
        NullAudioSink.Instance.Play(null!, at: -1d, gain: -1f);

        Assert.AreSame(NullAudioSink.Instance, env.Audio, "A stateless no-op needs exactly one instance.");
    }

    /// <summary>A scene that records the environment its lifecycle hooks could see.</summary>
    private sealed class EnvProbeScene : Scene
    {
        internal SceneEnvironment? EnvSeenInOnLoad { get; private set; }

        internal SceneEnvironment? EnvSeenInOnUnload { get; private set; }

        protected override void OnLoad() => EnvSeenInOnLoad = Env;

        protected override void OnUnload() => EnvSeenInOnUnload = Env;
    }

    private sealed class StubAssets : IAssets
    {
    }

    private sealed class StubAudioClip : IAudioClip
    {
    }

    private sealed class StubTypesetter : ITypesetter
    {
        public void SetDefaultFamilies(string textFamily, string mathFamily)
        {
        }
    }

    private sealed class StubAudioSink : IAudioSink
    {
        public void Play(IAudioClip clip, double at, float gain = 1f)
        {
        }
    }
}
