using Loom.Config;
using Loom.Core.Pipeline;

namespace Loom.Testing;

/// <summary>
///     <see cref="DependencyResolver" /> is the seam a package manager plugs into: it never touches a registry or a
///     network, only the directories it is handed for a project's <c>[dependencies]</c>, transitively. These tests
///     stand in for that tool by handing it directories written straight to a temp workspace.
/// </summary>
[Collection("Assembly")]
public class DependencyResolverTest
{
    private const string AppManifest = "project_type = \"game\"\n[dependencies]\nmath = \"^1.0\"\n";
    private const string MathManifest = "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n";

    [Fact]
    public void Resolves_ADirectDependency_IntoARootTheUnitCanCompile()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", AppManifest, [("main.loom", "import { pi } from \"math\";\nlet x = pi;")]);
        workspace.WriteProject("packages/math", MathManifest, [("init.loom", "export let pi = 3;")]);

        var roots = DependencyResolver.Resolve(app, workspace.DirectoriesByPackage, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(2, roots.Count);

        var unit = new CompilationUnit(roots);
        var result = unit.Compile();
        Utility.AssertNoErrors(result);
    }

    [Fact]
    public void Resolves_ATransitiveDependency_SoAnImportOfItAlsoResolves()
    {
        const string geometryManifest = "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n[dependencies]\nmath = \"^1.0\"\n";

        using var workspace = new Workspace();
        var app = workspace.WriteProject(
            "app",
            "project_type = \"game\"\n[dependencies]\ngeometry = \"^1.0\"\n",
            [("main.loom", "import { area } from \"geometry\";\nlet x = area;")]
        );
        workspace.WriteProject("packages/geometry", geometryManifest, [("init.loom", "import { pi } from \"math\";\nexport let area = pi;")]);
        workspace.WriteProject("packages/math", MathManifest, [("init.loom", "export let pi = 3;")]);

        var roots = DependencyResolver.Resolve(app, workspace.DirectoriesByPackage, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(3, roots.Count);

        var unit = new CompilationUnit(roots);
        Utility.AssertNoErrors(unit.Compile());
    }

    /// <summary>Two dependents resolving the same transitive package must not fight over it, or duplicate it.</summary>
    [Fact]
    public void Resolves_ADiamondDependency_OnceRatherThanTwice()
    {
        const string appManifest = "project_type = \"game\"\n[dependencies]\ngeometry = \"^1.0\"\nphysics = \"^1.0\"\n";
        const string geometryManifest = "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n[dependencies]\nmath = \"^1.0\"\n";
        const string physicsManifest = "project_type = \"library\"\n[package]\nname = \"physics\"\nversion = \"1.0.0\"\n[dependencies]\nmath = \"^1.0\"\n";

        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", appManifest, [("main.loom", "let x = 1;")]);
        workspace.WriteProject("packages/geometry", geometryManifest, [("init.loom", "import { pi } from \"math\";\nexport let area = pi;")]);
        workspace.WriteProject("packages/physics", physicsManifest, [("init.loom", "import { pi } from \"math\";\nexport let mass = pi;")]);
        workspace.WriteProject("packages/math", MathManifest, [("init.loom", "export let pi = 3;")]);

        var roots = DependencyResolver.Resolve(app, workspace.DirectoriesByPackage, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(4, roots.Count); // app + geometry + physics + math, math counted once
    }

    /// <summary>A cycle in what packages declare is not what forms an import cycle, so resolving it is not an error.</summary>
    [Fact]
    public void Resolves_AMutualDependency_WithoutRecursingForever()
    {
        const string aManifest = "project_type = \"library\"\n[package]\nname = \"a\"\nversion = \"1.0.0\"\n[dependencies]\nb = \"^1.0\"\n";
        const string bManifest = "project_type = \"library\"\n[package]\nname = \"b\"\nversion = \"1.0.0\"\n[dependencies]\na = \"^1.0\"\n";

        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", "project_type = \"game\"\n[dependencies]\na = \"^1.0\"\n", [("main.loom", "let x = 1;")]);
        workspace.WriteProject("packages/a", aManifest, [("init.loom", "export let value = 1;")]);
        workspace.WriteProject("packages/b", bManifest, [("init.loom", "export let value = 1;")]);

        var roots = DependencyResolver.Resolve(app, workspace.DirectoriesByPackage, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(3, roots.Count);
    }

    [Fact]
    public void Reports_ADependency_WithNoDirectoryResolvedForIt()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", AppManifest, [("main.loom", "let x = 1;")]);

        var roots = DependencyResolver.Resolve(app, workspace.DirectoriesByPackage, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("the project depends on 'math', but no directory was resolved for it.", diagnostic.Message);
    }

    [Fact]
    public void Reports_AResolvedDirectory_WithNoManifest()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", AppManifest, [("main.loom", "let x = 1;")]);
        var emptyDirectory = Path.Combine(workspace.Root, "empty");
        Directory.CreateDirectory(emptyDirectory);
        var packageDirectories = new Dictionary<PackageName, string> { [PackageName.Parse("math")] = emptyDirectory };

        var roots = DependencyResolver.Resolve(app, packageDirectories, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("has no loom-config.toml", diagnostic.Message);
    }

    [Fact]
    public void Reports_AResolvedDirectory_PublishingADifferentPackage()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", AppManifest, [("main.loom", "let x = 1;")]);
        workspace.WriteProject(
            "packages/geometry",
            "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n",
            [("init.loom", "export let area = 1;")]
        );

        var packageDirectories = new Dictionary<PackageName, string>
        {
            [PackageName.Parse("math")] = Path.Combine(workspace.Root, "packages", "geometry")
        };

        var roots = DependencyResolver.Resolve(app, packageDirectories, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("publishes 'geometry' instead", diagnostic.Message);
    }

    [Fact]
    public void Reports_AResolvedDirectory_WithNoPackageTable()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject("app", AppManifest, [("main.loom", "let x = 1;")]);
        workspace.WriteProject("packages/math", "project_type = \"game\"\n", [("main.loom", "let x = 1;")]);

        // unpublishable, so nothing registered it in DirectoriesByPackage under the name being tested here
        var packageDirectories = new Dictionary<PackageName, string> { [PackageName.Parse("math")] = Path.Combine(workspace.Root, "packages", "math") };

        var roots = DependencyResolver.Resolve(app, packageDirectories, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("publishes no package instead", diagnostic.Message);
    }

    /// <summary>A throwaway multi-project workspace, tracking every package it wrote so a test can hand its whole map to the resolver at once.</summary>
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());

        public Dictionary<PackageName, string> DirectoriesByPackage { get; } = [];

        public LoomConfig WriteProject(string relativeDirectory, string manifest, IEnumerable<(string Path, string Source)> files)
        {
            var directory = Path.Combine(Root, relativeDirectory);
            var sourceDirectory = Path.Combine(directory, "src");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(
                Path.Combine(directory, "loom-config.toml"),
                manifest + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );

            foreach (var (path, source) in files)
            {
                var filePath = Path.Combine(sourceDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, source);
            }

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            if (config.Package?.Name is { } name)
                DirectoriesByPackage[name] = directory;

            return config;
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
