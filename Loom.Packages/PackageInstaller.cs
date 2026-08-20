using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     Puts the packages a lock names where the compiler reads them: <see cref="PackageLayout" />'s directory per
///     package, holding the version the lock pins.
/// </summary>
public static class PackageInstaller
{
    /// <summary>
    ///     Installs every package <paramref name="lockFile" /> pins that is not already installed at the version it
    ///     pins, and answers whether the project's packages are now what the lock says.
    /// </summary>
    /// <remarks>
    ///     A package already installed at the locked version is left alone, so a build that has nothing to do costs
    ///     one manifest read per package rather than a copy of every one of them. That check is the *installed*
    ///     version against the lock — never a timestamp: a directory holding the right version is right however it
    ///     got there, which is what makes vendoring by hand and installing from an index the same thing downstream.
    /// </remarks>
    public static bool Install(LoomConfig project, LockFile lockFile, IPackageIndex index, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        foreach (var locked in lockFile.Packages)
        {
            var directory = PackageLayout.DirectoryOf(project, locked.Name);
            if (IsInstalled(directory, locked))
                continue;

            var publication = index.Publications(locked.Name).FirstOrDefault(candidate => candidate.Version.Equals(locked.Version));
            if (publication == null)
            {
                reported.Add(new ConfigDiagnostic($"'{locked.Name}' {locked.Version} is locked, but '{index.Description}' does not publish it."));
                continue;
            }

            if (!index.Install(publication, directory, out var installDiagnostics))
                reported.AddRange(installDiagnostics);
        }

        return reported.Count == 0;
    }

    /// <summary>Whether <paramref name="directory" /> already holds the package the lock pins, at that version.</summary>
    public static bool IsInstalled(string directory, LockedPackage locked)
    {
        var config = ConfigReader.LocateFromDirectory(directory, out _);
        return config?.Package is { } package && package.Name == locked.Name && locked.Version.Equals(package.Version);
    }
}
