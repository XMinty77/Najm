using System.Text.Json;
using System.Xml.Linq;

namespace Najm.Architecture.Tests;

[TestClass]
public sealed class ArchitectureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void ActiveProductionProjectsPointInward()
    {
        AssertProjectReferences("src/Najm.Utils/Najm.Utils.csproj");
        AssertProjectReferences(
            "src/Najm.Core/Najm.Core.csproj",
            "src/Najm.Utils/Najm.Utils.csproj");
        AssertProjectReferences(
            "src/Najm.Skia/Najm.Skia.csproj",
            "src/Najm.Core/Najm.Core.csproj");
        AssertProjectReferences(
            "src/Najm.Text/Najm.Text.csproj",
            "src/Najm.Core/Najm.Core.csproj");

        // §16: Lib depends on Core and Utils, and on nothing else — not on a backend, not on the
        // text assembly. That is what forces NAJM-TEXT I.1's authority pin rather than merely
        // asking for it: the only text-shaped things a TextNode in here can reach are Core's
        // ITypesetter capability and Core's Tier-1 DrawText op, so a node physically cannot shape.
        AssertProjectReferences(
            "src/Najm.Lib/Najm.Lib.csproj",
            "src/Najm.Core/Najm.Core.csproj",
            "src/Najm.Utils/Najm.Utils.csproj");
    }

    [TestMethod]
    public void PortableProjectsDoNotTakeBackendPackageDependencies()
    {
        var forbiddenPrefixes = new[] { "SkiaSharp", "Silk.NET", "HarfBuzzSharp", "CSharpMath" };

        foreach (var project in new[]
                 {
                     "src/Najm.Utils/Najm.Utils.csproj",
                     "src/Najm.Core/Najm.Core.csproj",
                     "src/Najm.Lib/Najm.Lib.csproj",
                 })
        {
            var document = XDocument.Load(Path.Combine(RepositoryRoot, project));
            var packages = document
                .Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(name => name is not null)
                .Cast<string>();

            foreach (var package in packages)
            {
                Assert.IsFalse(
                    forbiddenPrefixes.Any(prefix =>
                        package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                    $"Project '{project}' must not reference backend package '{package}'.");
            }

            AssertLockedGraphExcludes(project, forbiddenPrefixes);
        }
    }

    [TestMethod]
    public void TextProjectUsesOnlyPortableTextDependencies()
    {
        var allowedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HarfBuzzSharp",
            "HarfBuzzSharp.NativeAssets.Linux",
            "CSharpMath",
            "CSharpMath.Rendering",
        };
        var forbiddenPrefixes = new[] { "SkiaSharp", "Silk.NET", "CSharpMath.SkiaSharp" };
        var project = "src/Najm.Text/Najm.Text.csproj";
        var document = XDocument.Load(Path.Combine(RepositoryRoot, project));
        var packages = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        foreach (var package in packages)
        {
            Assert.IsFalse(
                forbiddenPrefixes.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                $"Project '{project}' must not reference backend package '{package}'.");
            Assert.Contains(
                package,
                allowedPackages,
                $"Project '{project}' has unreviewed package dependency '{package}'.");
        }

        AssertLockedGraphExcludes(project, forbiddenPrefixes);
    }

    /// <summary>
    /// The mechanism library takes no package dependency at all, not merely no backend one.
    /// </summary>
    /// <remarks>
    /// Stronger than the forbidden-prefix check above and deliberately so: §16 gives
    /// <c>Najm.Lib</c> exactly two project references and nothing else, so any package appearing
    /// here is a boundary decision that should be made on purpose rather than noticed later. A
    /// prefix list only catches the backends somebody already thought of.
    /// </remarks>
    [TestMethod]
    public void LibTakesNoPackageDependencyAtAll()
    {
        const string Project = "src/Najm.Lib/Najm.Lib.csproj";
        var document = XDocument.Load(Path.Combine(RepositoryRoot, Project));
        var packages = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(name => name is not null)
            .ToArray();

        Assert.IsEmpty(
            packages,
            $"Project '{Project}' must have no package references; it declares " +
            $"{string.Join(", ", packages)}.");
    }

    private static void AssertLockedGraphExcludes(string project, string[] forbiddenPrefixes)
    {
        var projectPath = Path.Combine(RepositoryRoot, project);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path '{projectPath}' has no directory.");
        var lockPath = Path.Combine(projectDirectory, "packages.lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));

        foreach (var target in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in target.Value.EnumerateObject())
            {
                var type = dependency.Value.GetProperty("type").GetString();
                if (string.Equals(type, "Project", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Assert.IsFalse(
                    forbiddenPrefixes.Any(prefix =>
                        dependency.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                    $"Project '{project}' must not resolve forbidden package '{dependency.Name}' " +
                    $"in target '{target.Name}'.");
            }
        }
    }

    private static void AssertProjectReferences(string project, params string[] expectedReferences)
    {
        var projectPath = Path.Combine(RepositoryRoot, project);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path '{projectPath}' has no directory.");
        var document = XDocument.Load(projectPath);
        var actual = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(path => path is not null)
            .Select(path => Path.GetFullPath(Path.Combine(projectDirectory, path!)))
            .ToArray();
        var expected = expectedReferences
            .Select(path => Path.GetFullPath(Path.Combine(RepositoryRoot, path)))
            .ToArray();

        CollectionAssert.AreEquivalent(
            expected,
            actual,
            $"Unexpected project dependency boundary for '{project}'.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Najm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
