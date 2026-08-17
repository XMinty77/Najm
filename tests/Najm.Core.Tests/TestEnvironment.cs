using Najm.Core.Tests.Delivery;

namespace Najm.Core.Tests;

/// <summary>Builds the environment a Core test loads a scene with.</summary>
/// <remarks>
/// <para>
/// Every scene loads against a <see cref="SceneEnvironment"/>, and the one capability an
/// environment cannot do without is its surface provider — the scene acquires its compositor from
/// it during load. Core has no backend, so tests supply the same backend-free
/// <see cref="StubSurfaceProvider"/> the offline-loop tests use and let Core's null objects fill
/// the other four capabilities. That is exactly the assembly a headless run wants, and it keeps
/// these tests measuring lifecycle, traversal, and timing rather than pixels.
/// </para>
/// <para>
/// Each call builds a fresh provider: a compositor is scene-lifetime and the scene disposes the one
/// it acquired at unload, so sharing a provider across scenes would let one test's teardown be
/// visible in another's.
/// </para>
/// </remarks>
internal static class TestEnvironment
{
    /// <summary>Creates an environment over a fresh stub provider and Core's null capabilities.</summary>
    internal static SceneEnvironment Stub() => new(new StubSurfaceProvider());
}
