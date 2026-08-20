using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     The package manager a build runs before it compiles: it makes the project's lock file and its installed
///     packages agree with its manifest, and does nothing at all when they already do.
/// </summary>
/// <remarks>
///     This is the tool side of the line the compiler draws. It reads the same <c>loom-lock.toml</c> the compiler
///     reads and installs into the same <see cref="PackageLayout" /> directories the compiler compiles from, but
///     nothing in <c>Loom.Core</c> knows it exists: a build that has everything it needs never opens an index, so
///     compiling stays possible with no registry reachable at all.
/// </remarks>
public static class PackageManager
{
    /// <summary>
    ///     Brings <paramref name="project" /> to a state its build can start from: a lock covering the manifest, and
    ///     every locked package installed at the version locked. Answers whether it did, with
    ///     <paramref name="diagnostics" /> saying what stopped it.
    /// </summary>
    /// <remarks>
    ///     An index is opened only when something has to change — resolution to write, or a package to install — so
    ///     a project whose packages are all present builds offline, whatever its <c>[registry]</c> says.
    /// </remarks>
    public static bool Restore(LoomConfig project, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (project.Dependencies.Count == 0)
            return true;

        var lockFile = LockFileReader.LocateFromDirectory(project.ProjectDirectory, out var lockDiagnostics);
        if (lockDiagnostics.Count > 0)
        {
            diagnostics = lockDiagnostics;
            return false;
        }

        var covered = lockFile != null && lockFile.Satisfies(project);
        var missing = lockFile == null ? [] : NotInstalled(project, lockFile);
        if (covered && missing.Count == 0)
            return true;

        var index = PackageIndexes.Open(project, out var indexDiagnostics);
        if (index == null)
        {
            // said before the reason the index could not be opened, so the two together read as one problem: what
            // had to be done, and why it could not be
            diagnostics = [Needed(covered, missing), ..indexDiagnostics];
            return false;
        }

        // a lock that no longer covers the manifest is still worth keeping to: only the requirements that changed
        // need a different answer, and every other package staying put is what makes a build reproducible between
        // one edit and the next
        var resolved = covered ? lockFile : LockResolver.Resolve(project, index, lockFile, out diagnostics);
        if (resolved == null)
            return false;

        if (!ReferenceEquals(resolved, lockFile))
        {
            try
            {
                resolved.WriteTo(project.ProjectDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics = [new ConfigDiagnostic($"could not write {LockFile.FileName} to '{project.ProjectDirectory}': {exception.Message}")];
                return false;
            }
        }

        return PackageInstaller.Install(project, resolved, index, out diagnostics);
    }

    /// <summary>Every locked package whose directory does not hold the version locked.</summary>
    private static List<LockedPackage> NotInstalled(LoomConfig project, LockFile lockFile) =>
        lockFile.Packages.Where(locked => !PackageInstaller.IsInstalled(PackageLayout.DirectoryOf(project, locked.Name), locked)).ToList();

    /// <summary>What the index was wanted for, so a project that has none is told what it is missing out on.</summary>
    private static ConfigDiagnostic Needed(bool covered, List<LockedPackage> missing) =>
        covered
            ? new ConfigDiagnostic($"{string.Join(", ", missing.Select(locked => $"'{locked.Name}' {locked.Version}"))} is locked but not installed.")
            : new ConfigDiagnostic($"the project's dependencies have not been resolved into {LockFile.FileName}.");
}
