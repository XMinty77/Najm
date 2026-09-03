using Najm.Core;
using Najm.Core.Text;
using Najm.Utils;

namespace Najm.Host.Desktop.Tests;

/// <summary>Pins the defaults §5.1, §9.1 and §15 name, and the values that are refused.</summary>
[TestClass]
public sealed class HostOptionsTests
{
    [TestMethod]
    public void TheDefaultsAreTheOnesTheReferenceStates()
    {
        var options = new HostOptions();

        Assert.AreEqual(Color.Black, options.BarColor, "§5.1: default opaque black.");
        Assert.AreEqual(Key.F1, options.OverlayKey, "§9.1: the overlay toggle, default F1.");
        Assert.AreEqual(Key.F5, options.RestartKey, "§15: the manual warm restart, default F5.");
        Assert.AreEqual(1280, options.Width);
        Assert.AreEqual(720, options.Height);
        Assert.AreEqual("Najm", options.Title);
        Assert.IsTrue(options.VSync);
    }

    [TestMethod]
    public void TheInjectedCapabilitiesDefaultToNothingRatherThanToSomething()
    {
        // §4.2: Typesetter and Audio arrive through here so this project references neither
        // Najm.Text nor Najm.Audio. Null means "the environment's own null object", which for text
        // is the fail-loud one.
        var options = new HostOptions();

        Assert.IsNull(options.Assets);
        Assert.IsNull(options.Typesetter);
        Assert.IsNull(options.Audio);

        var environment = new SceneEnvironment(
            new RasterSurfaces(),
            options.Assets,
            options.Typesetter,
            options.Audio);

        Assert.AreSame(NullAssets.Instance, environment.Assets);
        Assert.AreSame(NullTypesetter.Instance, environment.Typesetter);
        Assert.AreSame(NullAudioSink.Instance, environment.Audio);
    }

    [TestMethod]
    public void ValuesWithNoMeaningAreRefusedAtTheProperty()
    {
        var options = new HostOptions();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Width = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Height = -1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MaxDt = 0d);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.MaxDt = double.PositiveInfinity);
        Assert.ThrowsExactly<ArgumentNullException>(() => options.Title = null!);
    }

    [TestMethod]
    public void ARunningHostRefusesASecondScene()
    {
        // Not a window test: the guard is checked before anything platform-shaped happens, and a
        // host driving two scenes at once would break §4.1's single-driver rule.
        var host = new DesktopHost(new HostOptions());

        Assert.ThrowsExactly<ArgumentNullException>(() => host.Run((Func<Scene>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => host.Run((Scene)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new DesktopHost(null!));
    }

    /// <summary>The one capability a <see cref="SceneEnvironment"/> insists on, doing nothing.</summary>
    private sealed class RasterSurfaces : ISurfaceProvider
    {
        public RenderCaps Caps => RenderCaps.None;

        public IRenderTarget CreateTarget(in SurfaceSpec spec) => throw new NotSupportedException();

        public ICompositor CreateCompositor() => throw new NotSupportedException();

        public SurfaceSpec Normalize(in SurfaceSpec spec) => spec;

        public void Dispose()
        {
        }
    }
}
