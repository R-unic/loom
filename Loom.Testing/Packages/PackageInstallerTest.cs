using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Installation: what the lock pins, put where the compiler reads it. What a lock says is not re-decided here —
///     resolution chose the versions, and this puts them in place.
/// </summary>
[Collection("Assembly")]
public class PackageInstallerTest
{
    /// <remarks>
    ///     The other half of the yank asymmetry <see cref="LockResolverTest.Keeps_AYankedVersion_ALockAlreadyPins" />
    ///     states: withdrawing a version stops it being chosen, and getting this backwards would break exactly the
    ///     builds already on it — the ones yanking exists to leave alone.
    /// </remarks>
    [Fact]
    public void Installs_AYankedVersion_TheLockPins()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = LockResolver.Resolve(project, new LocalPackageIndex(fixture.IndexDirectory), null, out _);

        var installed = PackageInstaller.Install(project, lockFile!, new YankingPackageIndex(fixture.IndexDirectory, "math@1.5.0"), out var diagnostics);

        Assert.True(installed);
        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.5.0"), fixture.InstalledVersion("math"));
    }

    [Fact]
    public void Reports_ALockedVersionTheIndexDoesNotPublish()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("9.9.9"), null, null, [])]);

        Assert.False(PackageInstaller.Install(project, lockFile, new LocalPackageIndex(fixture.IndexDirectory), out var diagnostics));

        Assert.Contains("does not publish it", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     An index that could not say what it publishes has not said the locked version is missing, and reporting it
    ///     as missing would send somebody whose network is down looking for a version that is there.
    /// </remarks>
    [Fact]
    public void Reports_AnIndexThatCouldNotAnswer_AgainstTheIndex()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = LockResolver.Resolve(project, new LocalPackageIndex(fixture.IndexDirectory), null, out _);

        Assert.False(PackageInstaller.Install(project, lockFile!, new UnreachablePackageIndex(), out var diagnostics));

        Assert.Equal(UnreachablePackageIndex.Reason, Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     The one thing a committed lock exists to catch: a version's bytes changed after somebody depended on them.
    ///     Whether what is <em>served</em> matches what the index states is the index's own job, and says nothing
    ///     about whether either still matches what was locked.
    /// </remarks>
    [Fact]
    public void Reports_AnIndexNowStatingAnotherChecksum_ForAVersionTheLockPins()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.5.0"), null, "sha256:aa", [])]);

        Assert.False(PackageInstaller.Install(project, lockFile, new ChecksummingPackageIndex(fixture.IndexDirectory, "sha256:bb"), out var diagnostics));

        Assert.Contains("contents have changed since it was locked", Assert.Single(diagnostics).Message);
        Assert.Null(fixture.InstalledVersion("math"));
    }

    /// <remarks>An index stating none has not said what it published, which is no better than stating a different one.</remarks>
    [Fact]
    public void Reports_AnIndexNowStatingNoChecksum_ForAVersionTheLockPins()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.5.0"), null, "sha256:aa", [])]);

        Assert.False(PackageInstaller.Install(project, lockFile, new LocalPackageIndex(fixture.IndexDirectory), out var diagnostics));

        Assert.Contains("no checksum at all", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Installs_AVersionWhoseChecksum_TheIndexStillStates()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.5.0"), null, "sha256:AA", [])]);

        // hex is a spelling of the digest rather than part of it, so the case it is written in is not a mismatch
        Assert.True(PackageInstaller.Install(project, lockFile, new ChecksummingPackageIndex(fixture.IndexDirectory, "sha256:aa"), out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.5.0"), fixture.InstalledVersion("math"));
    }

    /// <remarks>A lock predating checksums being recorded has nothing to disagree with, and is not a reason to refuse.</remarks>
    [Fact]
    public void Installs_AVersionTheLockRecordsNoChecksumFor()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.5.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var lockFile = new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.5.0"), null, null, [])]);

        Assert.True(PackageInstaller.Install(project, lockFile, new ChecksummingPackageIndex(fixture.IndexDirectory, "sha256:aa"), out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.5.0"), fixture.InstalledVersion("math"));
    }
}
