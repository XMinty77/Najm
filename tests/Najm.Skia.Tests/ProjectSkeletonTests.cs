using System.Reflection;

namespace Najm.Skia.Tests;

[TestClass]
public sealed class ProjectSkeletonTests
{
    [TestMethod]
    public void ReferencedAssemblyIsPresent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Najm.Skia.dll");

        Assert.IsTrue(File.Exists(path), $"Expected referenced assembly at '{path}'.");
        Assert.AreEqual("Najm.Skia", AssemblyName.GetAssemblyName(path).Name);
    }
}

