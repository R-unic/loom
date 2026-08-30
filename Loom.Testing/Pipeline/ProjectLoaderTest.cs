using Loom.Config;
using Loom.Core.Pipeline;
using Version = Loom.Config.Version;
using Loom.Testing;

namespace Loom.Testing.Pipeline;

/// <summary>
///     <see cref="ProjectLoader" /> is where the lock file meets the compiler: what a build compiles is the project
///     plus the packages <c>loom-lock.toml</c> pins, read out of <see cref="PackageLayout" />'s directories. These
///     tests stand in for the package manager by writing both — the installed sources and the lock naming them.
/// </summary>
[Collection("Assembly")]
public class ProjectLoaderTest
{
    private const string MathManifest = "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n";

    [Fact]
    public void Loads_APinnedDependency_IntoARootTheUnitCanCompile()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "import { pi } from \"math\";\nlet x = pi;")]);
        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock();

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(2, roots.Count);
        Utility.AssertNoErrors(new CompilationUnit(roots).Compile());
    }

    [Fact]
    public void Loads_AScopedPackage_FromTheDirectoryItsScopeNames()
    {
        const string tetherManifest = "project_type = \"library\"\n[package]\nname = \"alternativelua/tether\"\nversion = \"0.3.1\"\n";

        using var workspace = new Workspace();
        var app = workspace.WriteProject(
            ".",
            Dependent("\"alternativelua/tether\" = \"^0.3\""),
            [("main.loom", "import { send } from \"alternativelua/tether\";\nlet x = send;")]
        );

        workspace.Install(tetherManifest, [("init.loom", "export let send = 1;")]);
        workspace.WriteLock();

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(Path.Combine(workspace.Root, "packages", "alternativelua", "tether"), roots[1].Config.ProjectDirectory);
        Utility.AssertNoErrors(new CompilationUnit(roots).Compile());
    }

    [Fact]
    public void Loads_ATransitiveDependency_TheLockAlsoPins()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(
            ".",
            Dependent("geometry = \"^1.0\""),
            [("main.loom", "import { area } from \"geometry\";\nlet x = area;")]
        );

        workspace.Install(
            "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n[dependencies]\nmath = \"^1.0\"\n",
            [("init.loom", "import { pi } from \"math\";\nexport let area = pi;")]
        );

        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock();

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(3, roots.Count);
        Utility.AssertNoErrors(new CompilationUnit(roots).Compile());
    }

    /// <remarks>A lock covering more than a build reaches is a package manager's business, not an error.</remarks>
    [Fact]
    public void Loads_AProjectWhoseLockPinsMoreThanItNeeds()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock([..workspace.Installed, new LockedPackage(PackageName.Parse("runit"), Version.Parse("0.4.0"))]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Equal(2, roots.Count);
    }

    /// <remarks>Nothing was ever resolved, and nothing needs to be: a lock file would answer no question.</remarks>
    [Fact]
    public void Loads_AProjectWithNoDependencies_WithoutALockFile()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", "project_type = \"game\"\n", [("main.loom", "let x = 1;")]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(roots);
        Assert.Single(roots);
        Utility.AssertNoErrors(new CompilationUnit(roots).Compile());
    }

    [Fact]
    public void Reports_AProjectDeclaringDependencies_WithNoLockFile()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\"", "geometry = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(
            "the project depends on 'geometry', 'math', but has no loom-lock.toml; resolve its dependencies with a package manager to write one.",
            diagnostic.Message
        );
    }

    [Fact]
    public void Reports_ALockFileItCannotRead_WithoutResolvingAnything()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "let x = 1;")]);
        File.WriteAllText(Path.Combine(workspace.Root, LockFile.FileName), "version = 1\n[[package]]\nname = \"math\"\n");

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        Assert.Contains("'math' must specify a 'version'", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Reports_ADependency_ThatIsPinnedButNotInstalled()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.WriteLock([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("the project depends on 'math', which is not installed in", diagnostic.Message);
        Assert.Contains(Path.Combine("packages", "math"), diagnostic.Message);
    }

    /// <remarks>The manifest was edited since the last resolution, so the lock answers a question nobody asked.</remarks>
    [Fact]
    public void Reports_ARequirementTheLockNoLongerCovers()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^2.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock();

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        Assert.Equal(
            "loom-lock.toml does not cover what the project depends on: 'math' is locked at 1.0.0, which '^2.0' does not accept.",
            Assert.Single(diagnostics).Message
        );
    }

    [Fact]
    public void Reports_ADependencyTheLockDoesNotPinAtAll()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock([]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        Assert.Contains("the project depends on 'math', but no directory was resolved for it", Assert.Single(diagnostics).Message);
    }

    /// <remarks>What is on disk is not what was resolved, which only a package manager can put right.</remarks>
    [Fact]
    public void Reports_AnInstalledVersion_TheLockDoesNotName()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("math = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install("project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.4.0\"\n", [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]);

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        Assert.Equal("'math' is installed at 1.4.0, but loom-lock.toml locks 1.0.0.", Assert.Single(diagnostics).Message);
    }

    /// <remarks>A dependency's own requirements are measured against the same lock, and named as its own.</remarks>
    [Fact]
    public void Reports_ARequirementOfADependency_TheLockDoesNotCover()
    {
        using var workspace = new Workspace();
        var app = workspace.WriteProject(".", Dependent("geometry = \"^1.0\""), [("main.loom", "let x = 1;")]);
        workspace.Install(
            "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n[dependencies]\nmath = \"^2.0\"\n",
            [("init.loom", "export let area = 1;")]
        );

        workspace.Install(MathManifest, [("init.loom", "export let pi = 3;")]);
        workspace.WriteLock();

        var roots = ProjectLoader.Load(app, out var diagnostics);

        Assert.Null(roots);
        Assert.Equal(
            "loom-lock.toml does not cover what 'geometry' depends on: 'math' is locked at 1.0.0, which '^2.0' does not accept.",
            Assert.Single(diagnostics).Message
        );
    }

    private static string Dependent(params string[] dependencies) =>
        "project_type = \"game\"\n[dependencies]\n" + string.Join("\n", dependencies) + "\n";

    /// <summary>
    ///     A project directory laid out the way a package manager leaves one: packages installed under
    ///     <c>packages/</c>, and a lock file naming the versions it installed there.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        private readonly List<LockedPackage> _installed = [];

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());

        /// <summary>Every package installed so far, at the version installed - what a package manager would lock.</summary>
        public IReadOnlyList<LockedPackage> Installed => _installed;

        /// <summary>Installs a package the way a package manager would: its own project, under the layout the compiler reads.</summary>
        public void Install(string manifest, IEnumerable<(string Path, string Source)> files)
        {
            var config = WriteProject("pending", manifest, files);
            var package = config.Package!;
            var directory = PackageLayout.DirectoryOf(_entry!, package.Name!);
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(Path.Combine(Root, "pending"), directory);
            _installed.Add(new LockedPackage(package.Name!, package.Version!, dependencies: config.Dependencies.Keys));
        }

        /// <summary>Writes the lock a package manager would leave behind: every package installed, at the version installed.</summary>
        public void WriteLock(IEnumerable<LockedPackage>? packages = null) => new LockFile(packages ?? _installed).WriteTo(Root);

        public LoomConfig WriteProject(string relativeDirectory, string manifest, IEnumerable<(string Path, string Source)> files)
        {
            var directory = Path.GetFullPath(Path.Combine(Root, relativeDirectory));
            var sourceDirectory = Path.Combine(directory, "src");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(
                Path.Combine(directory, ConfigReader.ConfigFileName),
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
            _entry ??= config;
            return config;
        }

        private LoomConfig? _entry;

        public void Dispose() => Directory.Delete(Root, true);
    }
}
