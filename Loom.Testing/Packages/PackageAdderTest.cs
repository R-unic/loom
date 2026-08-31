using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Adding a dependency is two things at once: a line written into the manifest, and the restore that line asks
///     for. These check both, and that a request that cannot be met leaves neither behind.
/// </summary>
[Collection("Assembly")]
public class PackageAdderTest
{
    [Fact]
    public void Add_WritesCompatibilityWithTheNewestVersionPublished_AndInstallsIt()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.2.0");
        var project = fixture.WriteProject("");

        var added = Add(fixture, project, "math");

        var package = Assert.Single(added);
        Assert.Equal(VersionRequirement.Parse("^1.2.0"), package.Requirement);
        Assert.Equal(Version.Parse("1.2.0"), package.Version);
        Assert.Contains("math = \"^1.2.0\"", fixture.ReadManifest());
        Assert.Equal(Version.Parse("1.2.0"), fixture.InstalledVersion("math"));
        Assert.Equal("math ^1.2.0 (1.2.0)", package.ToString());
    }

    /// <remarks>A request with no opinion about the version is not a request for a pre-release.</remarks>
    [Fact]
    public void Add_PrefersAReleaseToANewerPrerelease()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.1.0-beta.1");
        var project = fixture.WriteProject("");

        var package = Assert.Single(Add(fixture, project, "math"));

        Assert.Equal(VersionRequirement.Parse("^1.0.0"), package.Requirement);
        Assert.Equal(Version.Parse("1.0.0"), package.Version);
    }

    [Fact]
    public void Add_KeepsTheRequirementItWasGiven_AsItWasWritten()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.2.0");
        var project = fixture.WriteProject("");

        var package = Assert.Single(Add(fixture, project, "math@^1.0"));

        Assert.Contains("math = \"^1.0\"", fixture.ReadManifest());
        Assert.Equal(Version.Parse("1.2.0"), package.Version);
    }

    [Fact]
    public void Add_WritesADevelopmentDependency_AsOne()
    {
        using var fixture = new PackageIndexFixture().Publish("runit", "0.4.1");
        var project = fixture.WriteProject("");

        var added = PackageAdder.Add(project, [new PackageRequest(PackageName.Parse("runit"), null, true)], out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.True(Assert.Single(added!).IsDevelopmentOnly);
        Assert.Contains("dev = true", fixture.ReadManifest());
        var updated = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _);
        Assert.True(updated!.Dependencies[PackageName.Parse("runit")].IsDevelopmentOnly);
    }

    /// <remarks>Asking for a version of something the project already uses is asking to move to it.</remarks>
    [Fact]
    public void Add_ReplacesTheRequirementOfAPackageAlreadyDependedUpon()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "2.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));

        var package = Assert.Single(Add(fixture, project, "math@^2.0"));

        Assert.Equal(Version.Parse("2.0.0"), package.Version);
        Assert.Equal(Version.Parse("2.0.0"), fixture.InstalledVersion("math"));
        Assert.DoesNotContain("^1.0", fixture.ReadManifest());
    }

    [Fact]
    public void Add_TheProjectCompilesAgainstWhatItAdded()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0", source: "export let pi = 3;");
        var project = fixture.WriteProject("", "import { pi } from \"math\";\nlet x: number = pi;");

        Assert.NotNull(Add(fixture, project, "math"));

        var updated = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;
        updated.NoEmit = true;
        var roots = ProjectLoader.Load(updated, out var loadDiagnostics);
        Assert.Empty(loadDiagnostics);
        Utility.AssertNoErrors(new CompilationUnit(roots!).Compile());
    }

    [Fact]
    public void Add_ReportsAPackageTheIndexDoesNotPublish_AndLeavesTheManifestAlone()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("");
        var manifest = fixture.ReadManifest();

        Assert.Null(PackageAdder.Add(project, [Request("serio")], out var diagnostics));

        Assert.Contains("'serio' is not published", Assert.Single(diagnostics).Message);
        Assert.Equal(manifest, fixture.ReadManifest());
    }

    [Fact]
    public void Add_ReportsARequirementNothingPublishedSatisfies()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.2.0");
        var project = fixture.WriteProject("");
        var manifest = fixture.ReadManifest();

        Assert.Null(PackageAdder.Add(project, [Request("math@^2.0")], out var diagnostics));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("no published version of 'math' satisfies '^2.0'", diagnostic.Message);
        Assert.Contains("publishes 1.0.0, 1.2.0", diagnostic.Message);
        Assert.Equal(manifest, fixture.ReadManifest());
    }

    /// <remarks>
    ///     The manifest has to be written before resolution can read it, so a request that only turns out to be
    ///     impossible once its own dependencies are resolved is what proves the manifest goes back.
    /// </remarks>
    [Fact]
    public void Add_RestoresTheManifest_WhenWhatItAskedForCannotBeResolved()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \"^1.0\"")
            .Publish("math", "2.0.0");

        var project = fixture.WriteProject("");
        var manifest = fixture.ReadManifest();

        Assert.Null(PackageAdder.Add(project, [Request("geometry")], out var diagnostics));

        Assert.Contains("no published version of 'math'", Assert.Single(diagnostics).Message);
        Assert.Equal(manifest, fixture.ReadManifest());
        Assert.DoesNotContain("geometry", fixture.ReadManifest());
    }

    [Fact]
    public void Add_ReportsAProjectAskedToDependOnItself()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("math", "1.0.0");

        Assert.Null(PackageAdder.Add(library, [Request("math")], out var diagnostics));

        Assert.Contains("cannot depend on itself", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Add_ReportsOnePackageNamedTwice()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("");

        Assert.Null(PackageAdder.Add(project, [Request("math@^1.0"), Request("math")], out var diagnostics));

        Assert.Contains("named more than once", Assert.Single(diagnostics).Message);
    }

    /// <remarks>Adding needs an index, since only an index knows what asking for a package would get you.</remarks>
    [Fact]
    public void Add_ReportsAProjectWithNoRegistry()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("", withRegistry: false);

        Assert.Null(PackageAdder.Add(project, [Request("math")], out var diagnostics));

        Assert.Contains("no [registry] index", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Add_ReportsWhenNoPackagesAreNamed()
    {
        using var fixture = new PackageIndexFixture();
        var project = fixture.WriteProject("");

        Assert.Null(PackageAdder.Add(project, [], out var diagnostics));

        Assert.Contains("name at least one package", Assert.Single(diagnostics).Message);
    }

    /// <remarks>Reading the manifest is a tool choosing to, the same way writing it is — an I/O failure is its to report.</remarks>
    [Fact]
    public void Add_ReportsAManifestThatCannotBeRead()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("");
        File.Delete(Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName));

        Assert.Null(PackageAdder.Add(project, [Request("math")], out var diagnostics));

        Assert.Contains("could not read", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     A dependency's value can span more than the line its key is on — a multi-line string, here — and a
    ///     one-line rewrite cannot touch it without taking the rest of it along, so the manifest is left for its
    ///     author to edit by hand instead.
    /// </remarks>
    [Fact]
    public void Add_ReportsADependencyEntryThatCannotBeRewrittenAsOneLine()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.2.0");
        var project = fixture.WriteProject("math = \"\"\"\n^1.0\n\"\"\"");
        var manifest = fixture.ReadManifest();

        Assert.Null(PackageAdder.Add(project, [Request("math")], out var diagnostics));

        Assert.Contains("cannot be rewritten", Assert.Single(diagnostics).Message);
        Assert.Equal(manifest, fixture.ReadManifest());
    }

    /// <remarks>An I/O failure writing the manifest is the caller's to report, same as one writing the lock.</remarks>
    [Fact]
    public void Add_ReportsAnIOFailure_WritingTheManifest()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("");
        var manifestPath = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);

        File.SetAttributes(manifestPath, FileAttributes.ReadOnly);
        try
        {
            Assert.Null(PackageAdder.Add(project, [Request("math")], out var diagnostics));
            Assert.Contains("could not write", Assert.Single(diagnostics).Message);
        }
        finally
        {
            File.SetAttributes(manifestPath, FileAttributes.Normal);
        }
    }

    /// <remarks>
    ///     The manifest is re-read from disk after being edited, rather than amended in memory, precisely so a
    ///     write that leaves it unreadable is caught here instead of resolving against a project state nobody wrote.
    /// </remarks>
    [Fact]
    public void Add_ReportsWhenTheManifestCannotBeReadBackAfterWritingIt()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("");
        var manifestPath = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("project_type = \"game\"", "project_type = \"nonsense\""));

        Assert.Null(PackageAdder.Add(project, [Request("math")], out var diagnostics));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("could not be read back"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("unknown project type"));
    }

    [Fact]
    public void Add_TakesSeveralPackagesAtOnce()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("serio", "2.1.0");
        var project = fixture.WriteProject("");

        var added = PackageAdder.Add(project, [Request("math"), Request("serio")], out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["math", "serio"], added!.Select(package => package.Name.ToString()));
        Assert.Equal(Version.Parse("1.0.0"), fixture.InstalledVersion("math"));
        Assert.Equal(Version.Parse("2.1.0"), fixture.InstalledVersion("serio"));
    }

    private static IReadOnlyList<AddedPackage> Add(PackageIndexFixture fixture, LoomConfig project, string request)
    {
        var added = PackageAdder.Add(project, [Request(request)], out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(added);
        Assert.NotNull(fixture.ReadLock());
        return added;
    }

    private static PackageRequest Request(string text)
    {
        Assert.True(PackageRequest.TryParse(text, false, out var request, out var error), error);
        return request;
    }
}
