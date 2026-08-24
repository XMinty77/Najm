using Najm.Core.Text;

namespace Najm.Core;

/// <summary>The closed, typed set of capabilities a host gives one loaded scene.</summary>
/// <remarks>
/// <para>
/// A scene is a portable program and a host is a platform. A scene needs time per tick, a target
/// per render, and exactly these five capabilities — nothing else. The set is closed on purpose:
/// five typed properties are compile-time discoverable and trivially wrappable by an embedder,
/// whereas a service registry would be neither. There is no <c>IWindowManager</c>, no
/// <c>IRenderManager</c>, and no service locator anywhere in the engine. Author-shaped data that is
/// genuinely open-ended does not belong here.
/// </para>
/// <para>
/// <strong>Hosts assemble what they own and inject what they don't.</strong> A backend host builds
/// <see cref="Assets"/>, <see cref="Surfaces"/>, and <see cref="Caps"/> natively, and receives
/// <see cref="Typesetter"/> and <see cref="Audio"/> from its options — which is what keeps a host
/// project from referencing the text and audio assemblies. Core ships fail-loud defaults for the
/// injected pair, <see cref="NullTypesetter"/> and <see cref="NullAudioSink"/>, so an environment is
/// always complete and an omission is reported by the capability that was omitted rather than by a
/// null reference somewhere downstream.
/// </para>
/// <para>
/// <see cref="Surfaces"/> is the one capability with no null object. Every scene renders, a render
/// needs a compositor, and a compositor comes from the provider — so an environment without one
/// describes a scene that cannot run, and the constructor refuses to build it.
/// </para>
/// <para>
/// An environment is immutable and environment-lifetime. <see cref="With"/> produces a decorated
/// copy — the mechanism by which an embedder swaps one capability, a recording sink over the real
/// audio or a math decorator over the real typesetter, without disturbing the rest.
/// </para>
/// </remarks>
public sealed class SceneEnvironment
{
    /// <summary>Creates a complete environment, filling every omitted capability with its null object.</summary>
    /// <param name="surfaces">
    /// The backend's surface <em>and</em> composition authority. Required: it is the only capability
    /// with nothing sensible to stand in for it.
    /// </param>
    /// <param name="assets">The asset store, or null for <see cref="NullAssets"/>.</param>
    /// <param name="typesetter">The typesetter, or null for the fail-loud <see cref="NullTypesetter"/>.</param>
    /// <param name="audio">The audio sink, or null for the silent <see cref="NullAudioSink"/>.</param>
    /// <param name="caps">What the host's targets can do beyond portable 2D.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surfaces"/> is null.</exception>
    public SceneEnvironment(
        ISurfaceProvider surfaces,
        IAssets? assets = null,
        ITypesetter? typesetter = null,
        IAudioSink? audio = null,
        RenderCaps caps = RenderCaps.None)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        Surfaces = surfaces;
        Assets = assets ?? NullAssets.Instance;
        Typesetter = typesetter ?? NullTypesetter.Instance;
        Audio = audio ?? NullAudioSink.Instance;
        Caps = caps;
    }

    /// <summary>Gets the asset store shared resources load and cache through. Never null.</summary>
    public IAssets Assets { get; }

    /// <summary>Gets the typesetter every piece of text measures, shapes, and lays out through. Never null.</summary>
    public ITypesetter Typesetter { get; }

    /// <summary>Gets the sink this scene's audio emissions are realized by. Never null.</summary>
    public IAudioSink Audio { get; }

    /// <summary>Gets the backend's surface and composition authority. Never null.</summary>
    public ISurfaceProvider Surfaces { get; }

    /// <summary>Gets what this host's targets can do beyond portable 2D.</summary>
    public RenderCaps Caps { get; }

    /// <summary>Returns a copy of this environment with the given capabilities replaced.</summary>
    /// <param name="surfaces">A replacement surface provider, or null to keep this one's.</param>
    /// <param name="assets">A replacement asset store, or null to keep this one's.</param>
    /// <param name="typesetter">A replacement typesetter, or null to keep this one's.</param>
    /// <param name="audio">A replacement audio sink, or null to keep this one's.</param>
    /// <param name="caps">Replacement capabilities, or null to keep this one's.</param>
    /// <returns>
    /// A new environment carrying the arguments that were supplied and this one's values —
    /// reference-identical, not copies — for the arguments that were not.
    /// </returns>
    /// <remarks>
    /// Omission means "keep", which is why <paramref name="caps"/> is nullable: without that,
    /// <see cref="RenderCaps.None"/> and "not given" would be the same argument and no decorator
    /// could leave the capability flags alone. Wrapping a capability is therefore a single named
    /// argument, and nothing else about the environment can drift while it happens.
    /// </remarks>
    public SceneEnvironment With(
        ISurfaceProvider? surfaces = null,
        IAssets? assets = null,
        ITypesetter? typesetter = null,
        IAudioSink? audio = null,
        RenderCaps? caps = null) =>
        new(
            surfaces ?? Surfaces,
            assets ?? Assets,
            typesetter ?? Typesetter,
            audio ?? Audio,
            caps ?? Caps);
}
