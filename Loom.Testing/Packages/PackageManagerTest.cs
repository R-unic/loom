using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Restore is what a build runs before it compiles: resolve if the lock does not cover the manifest, install
///     what the lock pins, and do nothing at all when both already hold. These go all the way to a compile, since
///     the point of the whole arrangement is that the compiler finds what the package manager left.
/// </summary>
[Collection("Assembly")]
public class PackageManagerTest
{
    [Fact]
    public void Restore_WritesALockAndInstallsThePackages_ThenTheProjectCompiles()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.2.0", source: "export let pi = 3;");
        var project = fixture.WriteProject("math = \"^1.0\"", "import { pi } from \"math\";\nlet x: number = pi;");

        Assert.True(PackageManager.Restore(project, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.2.0"), fixture.ReadLock()!.Find(PackageName.Parse("math"))!.Version);
        Assert.Equal(Version.Parse("1.2.0"), fixture.InstalledVersion("math"));

        var roots = ProjectLoader.Load(project, out var loadDiagnostics);
        Assert.Empty(loadDiagnostics);
        Utility.AssertNoErrors(new CompilationUnit(roots!).Compile());
    }

    [Fact]
    public void Restore_InstallsATransitiveDependency_TheProjectNeverNames()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "math = \"^1.0\"", "import { pi } from \"math\";\nexport let area = pi;")
            .Publish("math", "1.0.0", source: "export let pi = 3;");

        var project = fixture.WriteProject("geometry = \"^1.0\"", "import { area } from \"geometry\";\nlet x: number = area;");

        Assert.True(PackageManager.Restore(project, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.0.0"), fixture.InstalledVersion("math"));
        var roots = ProjectLoader.Load(project, out var loadDiagnostics);
        Assert.Empty(loadDiagnostics);
        Assert.Equal(3, roots!.Count);
        Utility.AssertNoErrors(new CompilationUnit(roots).Compile());
    }

    /// <remarks>The requirement that changed gets a new answer; every other package stays where it was.</remarks>
    [Fact]
    public void Restore_ReResolvesOnlyWhatAChangedRequirementForces()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("math", "1.0.0")
            .Publish("math", "1.5.0")
            .Publish("serio", "1.0.0")
            .Publish("serio", "2.0.0");

        var project = fixture.WriteProject("math = \"^1.0\"\nserio = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));
        Assert.Equal(Version.Parse("1.0.0"), fixture.ReadLock()!.Find(PackageName.Parse("serio"))!.Version);

        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("serio = \"^1.0\"", "serio = \"^2.0\""));
        var bumped = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;

        Assert.True(PackageManager.Restore(bumped, out var diagnostics));

        Assert.Empty(diagnostics);
        var lockFile = fixture.ReadLock()!;
        Assert.Equal(Version.Parse("2.0.0"), lockFile.Find(PackageName.Parse("serio"))!.Version);
        Assert.Equal(Version.Parse("1.5.0"), lockFile.Find(PackageName.Parse("math"))!.Version);
        Assert.Equal(Version.Parse("2.0.0"), fixture.InstalledVersion("serio"));
    }

    /// <remarks>
    ///     A build whose packages are all present never opens the index, so an unreachable registry is no reason it
    ///     cannot compile — which is the whole point of committing the lock.
    /// </remarks>
    [Fact]
    public void Restore_DoesNothing_WhenTheLockAndTheInstalledPackagesAlreadyHold()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));

        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("index = \"../index\"", "index = \"../nowhere\""));
        var offline = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;

        Assert.True(PackageManager.Restore(offline, out var diagnostics));
        Assert.Empty(diagnostics);
    }

    /// <remarks>A package deleted from under the lock is reinstalled at the version the lock still names.</remarks>
    [Fact]
    public void Restore_ReinstallsAPackageThatWentMissing_WithoutReResolving()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.9.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));

        var installed = PackageLayout.DirectoryOf(project, PackageName.Parse("math"));
        var chosen = fixture.ReadLock()!.Find(PackageName.Parse("math"))!.Version;
        Directory.Delete(installed, true);

        Assert.True(PackageManager.Restore(project, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(chosen, fixture.InstalledVersion("math"));
    }

    /// <remarks>What is installed is brought back to what was resolved, rather than the lock being rewritten to match it.</remarks>
    [Fact]
    public void Restore_ReinstallsOverAVersionThatIsNotTheOneLocked()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "1.9.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]).WriteTo(fixture.ProjectDirectory);

        Assert.True(PackageManager.Restore(project, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.0.0"), fixture.InstalledVersion("math"));
        Assert.Equal(Version.Parse("1.0.0"), fixture.ReadLock()!.Find(PackageName.Parse("math"))!.Version);
    }

    [Fact]
    public void Restore_DoesNothingForAProjectWithNoDependencies()
    {
        using var fixture = new PackageIndexFixture();
        var project = fixture.WriteProject("", withRegistry: false);

        Assert.True(PackageManager.Restore(project, out var diagnostics));
        Assert.Empty(diagnostics);
        Assert.Null(fixture.ReadLock());
    }

    [Fact]
    public void Restore_ReportsARequirementItCannotResolve()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^2.0\"");

        Assert.False(PackageManager.Restore(project, out var diagnostics));
        Assert.Contains("no published version of 'math'", Assert.Single(diagnostics).Message);
        Assert.Null(fixture.ReadLock());
    }

    [Fact]
    public void Restore_ReportsALockItCannotRead_WithoutTouchingTheIndex()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        File.WriteAllText(Path.Combine(fixture.ProjectDirectory, LockFile.FileName), "version = 1\n[[package]]\nname = \"math\"\n");

        Assert.False(PackageManager.Restore(project, out var diagnostics));
        Assert.Contains("'math' must specify a 'version'", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     A package's development dependencies are not installed for a consumer, so nothing has to be published for
    ///     them either — which is what makes them development-only in the first place.
    /// </remarks>
    [Fact]
    public void Restore_IgnoresTheDevelopmentDependenciesOfAPackage()
    {
        using var fixture = new PackageIndexFixture()
            .Publish("geometry", "1.0.0", "runit = { version = \"^0.4\", dev = true }", "export let area = 1;");

        var project = fixture.WriteProject("geometry = \"^1.0\"", "import { area } from \"geometry\";\nlet x: number = area;");

        Assert.True(PackageManager.Restore(project, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal(["geometry"], fixture.ReadLock()!.Packages.Select(package => package.Name.ToString()));
        var roots = ProjectLoader.Load(project, out var loadDiagnostics);
        Assert.Empty(loadDiagnostics);
        Utility.AssertNoErrors(new CompilationUnit(roots!).Compile());
    }

    /// <remarks>
    ///     Said together, so the two read as one problem: nothing has been resolved yet, and here is why an index
    ///     could not be opened to resolve it.
    /// </remarks>
    [Fact]
    public void Restore_ReportsAnUnresolvedProject_AlongsideWhyTheIndexCouldNotOpen()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("index = \"../index\"", "index = \"../nowhere\""));
        var broken = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;

        Assert.False(PackageManager.Restore(broken, out var diagnostics));

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("have not been resolved into"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("is not a directory"));
        Assert.Null(fixture.ReadLock());
    }

    /// <remarks>A lock that already covers the manifest still needs the index once a package it names goes missing.</remarks>
    [Fact]
    public void Restore_ReportsAMissingInstall_AlongsideWhyTheIndexCouldNotOpen()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));
        Directory.Delete(PackageLayout.DirectoryOf(project, PackageName.Parse("math")), true);

        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("index = \"../index\"", "index = \"../nowhere\""));
        var broken = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;

        Assert.False(PackageManager.Restore(broken, out var diagnostics));

        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("is locked but not installed"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("is not a directory"));
    }

    /// <remarks>Writing the lock is a tool choosing to, so an I/O failure doing it is the caller's to report.</remarks>
    [Fact]
    public void Restore_ReportsAnIOFailure_WritingTheLock()
    {
        using var fixture = new PackageIndexFixture().Publish("math", "1.0.0").Publish("math", "2.0.0");
        var project = fixture.WriteProject("math = \"^1.0\"");
        Assert.True(PackageManager.Restore(project, out _));

        var manifest = Path.Combine(fixture.ProjectDirectory, ConfigReader.ConfigFileName);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("math = \"^1.0\"", "math = \"^2.0\""));
        var bumped = ConfigReader.LocateFromDirectory(fixture.ProjectDirectory, out _)!;

        var lockPath = Path.Combine(fixture.ProjectDirectory, LockFile.FileName);
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);
        try
        {
            Assert.False(PackageManager.Restore(bumped, out var diagnostics));
            Assert.Contains($"could not write {LockFile.FileName}", Assert.Single(diagnostics).Message);
        }
        finally
        {
            File.SetAttributes(lockPath, FileAttributes.Normal);
        }
    }
}
