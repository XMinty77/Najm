namespace Najm.Skia.Tests.Gpu;

/// <summary>
/// Proves the offline GL bootstrap: a real OpenGL ES context with no window, no display server, and
/// no GPU, and a Skia GPU context over it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HeadlessGlContextTests
{
    [TestMethod]
    public void HeadlessContext_ComesUpAndReportsItsGlStrings()
    {
        using var fixture = GpuFixture.Require();
        var context = fixture.GlContext;

        Assert.IsTrue(context.IsCurrent, "The context must be current on the thread that created it.");
        Assert.AreEqual(Environment.CurrentManagedThreadId, context.OwnerThreadId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(context.Renderer), "GL_RENDERER must be reported.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(context.Version), "GL_VERSION must be reported.");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(context.ShadingLanguageVersion),
            "GL_SHADING_LANGUAGE_VERSION must be reported — the author's shaders are written against it.");

        // Not an assertion about which driver answered: a software rasterizer is a legitimate
        // answer here and is what this machine has. The point is that something coherent did.
        Console.WriteLine(
            $"GL_VENDOR = {context.Vendor}; GL_RENDERER = {context.Renderer}; "
            + $"GL_VERSION = {context.Version}; GLSL = {context.ShadingLanguageVersion}");
    }

    [TestMethod]
    public void HeadlessContext_ResolvesEntryPointsAndRefusesNonsenseOnes()
    {
        using var fixture = GpuFixture.Require();

        Assert.AreNotEqual(
            IntPtr.Zero,
            fixture.GlContext.GetProcAddress("glGetString"),
            "The proc loader is what Skia's GL interface is built over.");
        Assert.ThrowsExactly<ArgumentNullException>(() => fixture.GlContext.GetProcAddress(null!));
    }

    [TestMethod]
    public void GpuContextOverIt_ReportsASaneMaximumTextureSize()
    {
        using var fixture = GpuFixture.Require();

        var maxTextureSize = fixture.Provider.MaxTextureSize;

        Assert.AreEqual(maxTextureSize, fixture.Provider.NativeContext.MaxTextureSize);
        Assert.IsGreaterThanOrEqualTo(
            2048,
            maxTextureSize,
            "OpenGL ES 3.0 mandates at least 2048; anything less means the context is not what it claims.");
        Assert.IsLessThanOrEqualTo(
            1 << 20,
            maxTextureSize,
            "An absurd maximum means the value was not really queried.");
        Assert.IsFalse(fixture.Provider.NativeContext.IsAbandoned);
    }

    [TestMethod]
    public void Disposal_UnbindsTheContextAndIsIdempotent()
    {
        var fixture = GpuFixture.Require();
        var context = fixture.GlContext;
        fixture.Dispose();

        Assert.IsFalse(context.IsCurrent, "Disposal must leave the thread with no current context.");
        context.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.GetProcAddress("glGetString"));
        Assert.ThrowsExactly<ObjectDisposedException>(context.MakeCurrent);
    }

    [TestMethod]
    public void SupportedPlatform_MatchesTheOnlyPlatformThisBindingTargets()
    {
        Assert.AreEqual(OperatingSystem.IsLinux(), HeadlessGlContext.IsSupportedPlatform);
        if (!HeadlessGlContext.IsSupportedPlatform)
        {
            Assert.ThrowsExactly<PlatformNotSupportedException>(() => HeadlessGlContext.Create());
        }
    }
}
