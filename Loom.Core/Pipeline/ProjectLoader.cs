using Loom.Config;

namespace Loom.Core.Pipeline;

/// <summary>
///     What a project on disk compiles: its own source root, plus one per dependency its lock file pins. The one
///     place that reads <c>loom-lock.toml</c> for a build, so the CLI, a watch and the language server all answer
///     the question the same way.
/// </summary>
public static class ProjectLoader
{
    /// <summary>
    ///     Loads the roots to compile <paramref name="entry" />, or <see langword="null" /> alongside the
    ///     <paramref name="diagnostics" /> saying why — a malformed lock, a dependency not installed, a lock that no
    ///     longer covers the manifest. Never an exception, like every other way the compiler reads a manifest.
    /// </summary>
    /// <remarks>
    ///     A project with no dependencies needs no lock file and is loaded without one; a project that declares
    ///     dependencies and has no lock has never been resolved, which is a package manager's job to do and not
    ///     something to guess at from the requirements alone — two builds guessing would be exactly what the lock
    ///     exists to prevent.
    /// </remarks>
    public static SourceRootSet? Load(LoomConfig entry, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var lockFile = LockFileReader.LocateFromDirectory(entry.ProjectDirectory, out diagnostics);
        if (diagnostics.Count > 0)
            return null;

        if (lockFile != null)
            return DependencyResolver.Resolve(entry, lockFile, out diagnostics);

        if (entry.Dependencies.Count == 0)
            return new SourceRootSet(new SourceRoot(entry));

        var names = string.Join(", ", entry.Dependencies.Keys.Order().Select(name => $"'{name}'"));
        diagnostics =
        [
            new ConfigDiagnostic(
                $"the project depends on {names}, but has no {LockFile.FileName}; resolve its dependencies with a package manager to write one."
            )
        ];

        return null;
    }
}
