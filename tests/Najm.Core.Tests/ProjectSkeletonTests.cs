using System.Reflection;

namespace Najm.Core.Tests;

[TestClass]
public sealed class ProjectSkeletonTests
{
    [TestMethod]
    public void ReferencedAssemblyIsPresent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Najm.Core.dll");

        Assert.IsTrue(File.Exists(path), $"Expected referenced assembly at '{path}'.");
        Assert.AreEqual("Najm.Core", AssemblyName.GetAssemblyName(path).Name);
    }
}

