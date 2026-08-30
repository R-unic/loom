using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Publishing is what a package is made of, and then an index taking it. A published version is source — an
///     index's versions are Loom projects — so what these check is that what lands in the index is a project the
///     compiler can read, and that a version already there is never replaced.
/// </summary>
[Collection("Assembly")]
public class PackagePublisherTest
{
    [Fact]
    public void Prepare_TakesTheManifestAndTheSources()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("math", "1.2.0");
        File.WriteAllText(Path.Combine(fixture.LibraryDirectory, "src", "vector.loom"), "export let zero = 0;");
        File.WriteAllText(Path.Combine(fixture.LibraryDirectory, "README.md"), "# math");

        var payload = PackagePublisher.Prepare(library, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(PackageName.Parse("math"), payload!.Name);
        Assert.Equal(Version.Parse("1.2.0"), payload.Version);
        Assert.Equal(
            [ConfigReader.ConfigFileName, Path.Combine("src", "init.loom"), Path.Combine("src", "vector.loom"), "README.md"],
            payload.Files
        );
    }

    /// <remarks>
    ///     Compiled output is one consumer's build and a lock file is one project's answer, so neither is any use to
    ///     whoever installs the package.
    /// </remarks>
    [Fact]
    public void Prepare_LeavesTheOutputTheLockAndTheInstalledPackagesBehind()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("math", "1.0.0");
        Directory.CreateDirectory(Path.Combine(fixture.LibraryDirectory, "dist"));
        File.WriteAllText(Path.Combine(fixture.LibraryDirectory, "dist", "init.luau"), "return {}");
        Directory.CreateDirectory(Path.Combine(fixture.LibraryDirectory, FilesConfig.PackagesDirectoryName, "serio"));
        File.WriteAllText(Path.Combine(fixture.LibraryDirectory, FilesConfig.PackagesDirectoryName, "serio", "loom-config.toml"), "");
        File.WriteAllText(Path.Combine(fixture.LibraryDirectory, LockFile.FileName), "version = 1\n");

        var payload = PackagePublisher.Prepare(library, out _);

        Assert.Equal([ConfigReader.ConfigFileName, Path.Combine("src", "init.loom")], payload!.Files);
    }

    [Fact]
    public void Prepare_ReportsAProjectWithNoIdentityToPublishUnder()
    {
        using var fixture = new PackageIndexFixture();
        var project = fixture.WriteProject("");

        Assert.Null(PackagePublisher.Prepare(project, out var diagnostics));

        Assert.Contains("no [package] table", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Prepare_ReportsASourceDirectoryWithNoLoomFiles()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("math", "1.0.0");
        File.Delete(Path.Combine(fixture.LibraryDirectory, "src", "init.loom"));

        Assert.Null(PackagePublisher.Prepare(library, out var diagnostics));

        Assert.Contains("holds no .loom files", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     The whole point of publishing: what the index answers with afterwards is a version something else can
    ///     depend on, resolve and compile against.
    /// </remarks>
    [Fact]
    public void Publish_MakesTheVersionOneAnotherProjectCanDependOn()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("math", "1.2.0", source: "export let pi = 3;");
        var index = new LocalPackageIndex(fixture.IndexDirectory);

        Assert.True(PackagePublisher.Publish(PackagePublisher.Prepare(library, out _)!, index, out var diagnostics));

        Assert.Empty(diagnostics);
        var publication = Assert.Single(index.Publications(PackageName.Parse("math")));
        Assert.Equal(Version.Parse("1.2.0"), publication.Version);

        var consumer = fixture.WriteProject("math = \"^1.0\"", "import { pi } from \"math\";\nlet x: number = pi;");
        Assert.True(PackageManager.Restore(consumer, out var restoreDiagnostics));
        Assert.Empty(restoreDiagnostics);
        var roots = ProjectLoader.Load(consumer, out var loadDiagnostics);
        Assert.Empty(loadDiagnostics);
        Utility.AssertNoErrors(new CompilationUnit(roots!).Compile());
    }

    [Fact]
    public void Publish_PublishesTheRequirementsTheVersionDeclares()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("geometry", "1.0.0", "math = \"^1.2\"\nrunit = { version = \"^0.4\", dev = true }");
        var index = new LocalPackageIndex(fixture.IndexDirectory);

        Assert.True(PackagePublisher.Publish(PackagePublisher.Prepare(library, out _)!, index, out _));

        var publication = Assert.Single(index.Publications(PackageName.Parse("geometry")));
        var dependency = Assert.Single(publication.Dependencies);
        Assert.Equal(PackageName.Parse("math"), dependency.Name);
    }

    [Fact]
    public void Publish_RefusesAVersionAlreadyPublished()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var library = fixture.WriteLibrary("math", "1.0.0");
        var index = new LocalPackageIndex(fixture.IndexDirectory);

        Assert.False(PackagePublisher.Publish(PackagePublisher.Prepare(library, out _)!, index, out var diagnostics));

        Assert.Contains("already published", Assert.Single(diagnostics).Message);
    }

    /// <remarks>A scoped package is published where its scope says it is, which is where it is read back from.</remarks>
    [Fact]
    public void Publish_PutsAScopedPackageUnderItsScope()
    {
        using var fixture = new PackageIndexFixture();
        var library = fixture.WriteLibrary("alternativelua/tether", "0.3.1");
        var index = new LocalPackageIndex(fixture.IndexDirectory);

        Assert.True(PackagePublisher.Publish(PackagePublisher.Prepare(library, out _)!, index, out _));

        Assert.True(File.Exists(Path.Combine(fixture.IndexDirectory, "alternativelua", "tether", "0.3.1", ConfigReader.ConfigFileName)));
        Assert.Single(index.Publications(PackageName.Parse("alternativelua/tether")));
    }
}
