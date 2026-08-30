using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Resolution: requirements plus what an index publishes, in, one version per package out. The index is a
///     directory of fixtures, so what is under test is the choosing rather than any way of reaching a registry.
/// </summary>
[Collection("Assembly")]
public class LockResolverTest
{
    [Fact]
    public void Chooses_TheNewestPublishedVersion_TheRequirementAccepts()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.4.2").Publish("math", "2.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");

        var resolved = Resolve(project, fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(resolved);
        Assert.Equal(Version.Parse("1.4.2"), resolved.Find(PackageName.Parse("math"))!.Version);
    }

    /// <remarks>A pre-release is only ever chosen by a requirement that names one, which is the requirement's rule.</remarks>
    [Fact]
    public void Chooses_AReleaseOverAPrerelease_UnlessTheRequirementNamesOne()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.1.0-beta.1");
        var release = Resolve(fixture.WriteProject("math = \"^1.0\""), fixture, out _);
        Assert.Equal(Version.Parse("1.0.0"), release!.Find(PackageName.Parse("math"))!.Version);

        using var prerelease = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.1.0-beta.1");
        var chosen = Resolve(prerelease.WriteProject("math = \">=1.1.0-beta.1, <1.2\""), prerelease, out _);
        Assert.Equal(Version.Parse("1.1.0-beta.1"), chosen!.Find(PackageName.Parse("math"))!.Version);
    }

    [Fact]
    public void Resolves_ATransitiveDependency_IntoAClosedLock()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \"^1.0\"")
            .Publish("math", "1.2.0");

        var resolved = Resolve(fixture.WriteProject("geometry = \"^1.0\""), fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.NotNull(resolved);
        Assert.Equal(["geometry", "math"], resolved.Packages.Select(package => package.Name.ToString()));
        Assert.Equal([PackageName.Parse("math")], resolved.Find(PackageName.Parse("geometry"))!.Dependencies);

        // a lock is only a lock if it reads back as one
        var reread = LockFileReader.Read(resolved.ToToml(), out var readDiagnostics);
        Assert.Empty(readDiagnostics);
        Assert.NotNull(reread);
    }

    /// <remarks>Two dependents, one answer: the requirement resolution measures is the intersection of both.</remarks>
    [Fact]
    public void Chooses_AVersionSatisfyingEveryDependentAtOnce()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \">=1.0, <1.3\"")
            .Publish("math", "1.0.0")
            .Publish("math", "1.2.0")
            .Publish("math", "1.9.0");

        var resolved = Resolve(fixture.WriteProject("geometry = \"^1.0\"\nmath = \"^1.1\""), fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.2.0"), resolved!.Find(PackageName.Parse("math"))!.Version);
    }

    [Fact]
    public void Reports_TwoRequirementsNothingCanSatisfy()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \"^2.0\"")
            .Publish("math", "1.0.0")
            .Publish("math", "2.0.0");

        var resolved = Resolve(fixture.WriteProject("geometry = \"^1.0\"\nmath = \"^1.0\""), fixture, out var diagnostics);

        Assert.Null(resolved);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("no version of 'math' satisfies every requirement on it", diagnostic.Message);
        Assert.Contains("the project requires '^1.0'", diagnostic.Message);
        Assert.Contains("'geometry 1.0.0' requires '^2.0'", diagnostic.Message);
    }

    [Fact]
    public void Reports_ARequirementNoPublishedVersionMeets()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.2.0");

        var resolved = Resolve(fixture.WriteProject("math = \"^3.0\""), fixture, out var diagnostics);

        Assert.Null(resolved);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("no published version of 'math' satisfies '>=3.0.0, <4.0.0'", diagnostic.Message);
        Assert.Contains("publishes 1.0.0, 1.2.0", diagnostic.Message);
    }

    [Fact]
    public void Reports_APackageTheIndexDoesNotPublish()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");

        var resolved = Resolve(fixture.WriteProject("serio = \"^1.0\""), fixture, out var diagnostics);

        Assert.Null(resolved);
        Assert.Contains("'serio' is not published in", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     The project is the one being developed, so its own development dependencies are resolved; a package's are
    ///     what its tests are written against and no part of compiling it for someone else.
    /// </remarks>
    [Fact]
    public void Resolves_TheProjectsDevelopmentDependencies_ButNotAPackages()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "runit = { version = \"^0.4\", dev = true }")
            .Publish("runit", "0.4.0");

        var resolved = Resolve(fixture.WriteProject("geometry = \"^1.0\""), fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["geometry"], resolved!.Packages.Select(package => package.Name.ToString()));

        using var withDev = new PackageIndexFixture().Publish("runit", "0.4.0");
        var project = withDev.WriteProject("runit = { version = \"^0.4\", dev = true }");
        Assert.Equal(["runit"], Resolve(project, withDev, out _)!.Packages.Select(package => package.Name.ToString()));
    }

    /// <remarks>
    ///     Re-resolving after one requirement changes must not move every other package: a build that bumps what
    ///     nobody asked it to bump is a build nobody can reproduce.
    /// </remarks>
    [Fact]
    public void Keeps_AnAlreadyLockedVersion_ThatStillSatisfiesEveryRequirement()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.9.0").Publish("serio", "2.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"\nserio = \"^2.0\"");
        var preferred = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]);

        var resolved = LockResolver.Resolve(project, new LocalPackageIndex(fixture.IndexDirectory), preferred, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.0.0"), resolved!.Find(PackageName.Parse("math"))!.Version);
        Assert.Equal(Version.Parse("2.0.0"), resolved.Find(PackageName.Parse("serio"))!.Version);
    }

    [Fact]
    public void Replaces_AnAlreadyLockedVersion_TheRequirementNoLongerAccepts()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "2.1.0");
        var project = fixture.WriteProject("math = \"^2.0\"");
        var preferred = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]);

        var resolved = LockResolver.Resolve(project, new LocalPackageIndex(fixture.IndexDirectory), preferred, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("2.1.0"), resolved!.Find(PackageName.Parse("math"))!.Version);
    }

    /// <remarks>Nothing requires the declared graph to be acyclic, so resolution has to settle on one anyway.</remarks>
    [Fact]
    public void Settles_OnAGraphThatDependsOnItself()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \"^1.0\"")
            .Publish("math", "1.0.0", "geometry = \"^1.0\"");

        var resolved = Resolve(fixture.WriteProject("geometry = \"^1.0\""), fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["geometry", "math"], resolved!.Packages.Select(package => package.Name.ToString()));
    }

    [Fact]
    public void Resolves_AProjectWithNoDependencies_IntoAnEmptyLock()
    {
        using var fixture = new PackageIndexFixture();

        var resolved = Resolve(fixture.WriteProject(""), fixture, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(resolved!.Packages);
    }

    private static LockFile? Resolve(LoomConfig project, PackageIndexFixture fixture, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        LockResolver.Resolve(project, new LocalPackageIndex(fixture.IndexDirectory), null, out diagnostics);
}
