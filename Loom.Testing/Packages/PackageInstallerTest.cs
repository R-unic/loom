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
}
