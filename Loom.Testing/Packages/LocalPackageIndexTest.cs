using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing;

/// <summary>
///     A local index is a directory of published versions, each a Loom project of its own — so what it publishes is
///     stated by the same manifest the compiler reads, and installing is a copy.
/// </summary>
[Collection("Assembly")]
public class LocalPackageIndexTest
{
    [Fact]
    public void Publications_AreEveryVersionPublished_Ordered()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.10.0").Publish("math", "1.2.0").Publish("math", "1.0.0-beta.1");

        var publications = Index(fixture).Publications(PackageName.Parse("math"));

        Assert.Equal(["1.0.0-beta.1", "1.2.0", "1.10.0"], publications.Select(publication => publication.Version.ToString()));
    }

    [Fact]
    public void Publications_ReadTheRequirementsAVersionDeclares()
    {
        using var fixture = new PackageIndexFixture().Publish("geometry", "1.0.0", "math = \"^1.2\"\nrunit = { version = \"^0.4\", dev = true }");

        var publication = Assert.Single(Index(fixture).Publications(PackageName.Parse("geometry")));

        // development-only requirements are no part of compiling the package for someone else
        var dependency = Assert.Single(publication.Dependencies);
        Assert.Equal(PackageName.Parse("math"), dependency.Name);
        Assert.Equal(VersionRequirement.Parse("^1.2"), dependency.VersionRequirement);
    }

    [Fact]
    public void Publications_OfAScopedPackage_ComeFromTheDirectoryItsScopeNames()
    {
        using var fixture = new PackageIndexFixture().Publish("alternativelua/tether", "0.3.1");

        var publication = Assert.Single(Index(fixture).Publications(PackageName.Parse("alternativelua/tether")));

        Assert.Equal(Version.Parse("0.3.1"), publication.Version);
    }

    [Fact]
    public void Publications_OfAPackageTheIndexDoesNotHave_AreNone() =>
        Assert.Empty(Index(new PackageIndexFixture()).Publications(PackageName.Parse("math")));

    /// <remarks>An index may hold anything beside its versions, and a build has nothing to say about that.</remarks>
    [Fact]
    public void Publications_SkipADirectoryThatIsNotAVersion()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        Directory.CreateDirectory(Path.Combine(fixture.IndexDirectory, "math", "docs"));

        Assert.Single(Index(fixture).Publications(PackageName.Parse("math")));
    }

    /// <remarks>Which of the two a dependent asked for could not be said, so the version is not published at all.</remarks>
    [Fact]
    public void Publications_SkipAVersionWhoseManifestDisagreesWithItsDirectory()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var manifest = Path.Combine(fixture.IndexDirectory, "math", "1.0.0", ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("version = \"1.0.0\"", "version = \"1.0.1\""));

        Assert.Empty(Index(fixture).Publications(PackageName.Parse("math")));
    }

    [Fact]
    public void Install_CopiesEveryFileOfTheVersion()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0", source: "export let pi = 3;");
        var publication = Assert.Single(Index(fixture).Publications(PackageName.Parse("math")));
        var directory = Path.Combine(fixture.Root, "installed");

        Assert.True(Index(fixture).Install(publication, directory, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal("export let pi = 3;", File.ReadAllText(Path.Combine(directory, "src", "init.loom")));
        Assert.Equal(Version.Parse("1.0.0"), ConfigReader.LocateFromDirectory(directory, out _)!.Package!.Version);
    }

    /// <remarks>What is installed has to be one version, not the union of it and whatever was there before.</remarks>
    [Fact]
    public void Install_ReplacesWhatWasInstalledBefore()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "2.0.0");
        var index = Index(fixture);
        var directory = Path.Combine(fixture.Root, "installed");
        var publications = index.Publications(PackageName.Parse("math"));

        Assert.True(index.Install(publications[0], directory, out _));
        File.WriteAllText(Path.Combine(directory, "src", "left-behind.loom"), "let x = 1;");
        Assert.True(index.Install(publications[1], directory, out _));

        Assert.Equal(Version.Parse("2.0.0"), ConfigReader.LocateFromDirectory(directory, out _)!.Package!.Version);
        Assert.False(File.Exists(Path.Combine(directory, "src", "left-behind.loom")));
    }

    [Fact]
    public void Open_ReadsAnIndexPathRelativeToTheProject()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");

        var index = PackageIndexes.Open(project, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(index);
        Assert.Single(index.Publications(PackageName.Parse("math")));
    }

    [Fact]
    public void Open_ReportsAProjectWithNoRegistryTable()
    {
        using var fixture = new PackageIndexFixture();
        var project = fixture.WriteProject("math = \"^1.0\"", withRegistry: false);

        Assert.Null(PackageIndexes.Open(project, out var diagnostics));
        Assert.Contains("no [registry] index", Assert.Single(diagnostics).Message);
    }

    /// <remarks>Nothing here fetches over a network yet, and a build has to say so rather than resolve to nothing.</remarks>
    [Fact]
    public void Open_ReportsARemoteIndex_AsNotSupportedYet()
    {
        using var fixture = new PackageIndexFixture();
        fixture.WriteProject("math = \"^1.0\"");
        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("index = \"../index\"", $"index = \"{RegistryConfig.DefaultIndex}\""));
        var project = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _);

        Assert.Null(PackageIndexes.Open(project!, out var diagnostics));
        Assert.Contains("is not supported yet", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Open_ReportsAnIndexPathThatIsNotADirectory()
    {
        using var fixture = new PackageIndexFixture();
        fixture.WriteProject("math = \"^1.0\"");
        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("index = \"../index\"", "index = \"../nowhere\""));
        var project = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _);

        Assert.Null(PackageIndexes.Open(project!, out var diagnostics));
        Assert.Contains("is not a directory", Assert.Single(diagnostics).Message);
    }

    private static IPackageIndex Index(PackageIndexFixture fixture) => new LocalPackageIndex(fixture.IndexDirectory);
}
