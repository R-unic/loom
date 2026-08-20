using Loom.Config;

namespace Loom.Core.Pipeline;

/// <summary>
///     Turns an entry project's <c>[dependencies]</c>, transitively, into the extra <see cref="SourceRoot" />s a
///     <see cref="SourceRootSet" /> needs to compile them. This is the seam a package manager plugs into: matching a
///     version requirement against what is published, fetching it and deciding where it lands on disk is that tool's
///     job (see <see cref="Dependency.VersionRequirement" />), never the compiler's — all this needs from it is the
///     directory it decided on, one per package name for the whole build. <see cref="SourceRootSet.WithPackage" />
///     already assumes exactly one root can publish a given name, so a build wanting two different resolved versions
///     of the same package is not a shape this — or anything downstream — supports.
/// </summary>
/// <remarks>
///     A build of its own goes through <see cref="Resolve(LoomConfig, LockFile, out IReadOnlyList{ConfigDiagnostic})" />
///     instead, which takes those directories from <see cref="PackageLayout" /> and holds what is installed in them
///     against the lock. The overload taking a map is what a package manager — and every test standing in for one —
///     calls when it has decided the directories itself.
/// </remarks>
public static class DependencyResolver
{
    /// <summary>
    ///     Resolves what <paramref name="entry" /> compiles from its lock file: the packages <paramref name="lockFile" />
    ///     pins, read out of the directories <see cref="PackageLayout" /> says they are installed in, and held against
    ///     the lock once read. A build takes this path — the lock is what makes two machines compile the same versions
    ///     of the same sources.
    /// </summary>
    /// <remarks>
    ///     Two things are checked that the map overload cannot know to check: that every requirement a manifest in the
    ///     build writes is one the lock accepts, so a manifest edited since the last resolution is reported rather
    ///     than quietly compiled against the old answer; and that the package installed in a directory is the version
    ///     the lock names, so a directory changed underneath the lock is reported too. Either is a stale lock, which
    ///     only a package manager can fix, so neither is something to compile through.
    /// </remarks>
    public static SourceRootSet? Resolve(LoomConfig entry, LockFile lockFile, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        var roots = Resolve(entry, PackageLayout.DirectoriesOf(entry, lockFile), out var resolutionDiagnostics);
        reported.AddRange(resolutionDiagnostics);
        if (roots == null)
            return null;

        foreach (var root in roots)
            CheckAgainstLock(root, root == roots.Entry, lockFile, reported);

        return reported.Count == 0 ? roots : null;
    }

    /// <summary>
    ///     Resolves <paramref name="entry" /> against <paramref name="packageDirectories" />, walking every
    ///     dependency's own <c>[dependencies]</c> in turn, and returns the <see cref="SourceRootSet" /> to compile
    ///     — or <see langword="null" /> alongside <paramref name="diagnostics" /> explaining why, never an
    ///     exception: a manifest problem is reported the same way everywhere else the compiler reads one.
    /// </summary>
    /// <param name="entry">The project being built.</param>
    /// <param name="packageDirectories">
    ///     Where each package the build could need already lives on disk, keyed by the identity its own
    ///     <c>[package]</c> table publishes — fetched, vendored or cached, the compiler does not ask. Only entries a
    ///     dependency actually reaches are read, so this may cover more packages than any one build uses.
    /// </param>
    public static SourceRootSet? Resolve(
        LoomConfig entry,
        IReadOnlyDictionary<PackageName, string> packageDirectories,
        out IReadOnlyList<ConfigDiagnostic> diagnostics
    )
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        var resolved = new Dictionary<PackageName, LoomConfig>();
        ResolveDependenciesOf(entry, "the project", isEntry: true, packageDirectories, resolved, reported);

        return reported.Count == 0
            ? new SourceRootSet(new SourceRoot(entry), resolved.Values.Select(config => new SourceRoot(config)))
            : null;
    }

    /// <summary>
    ///     Resolves one config's own <c>[dependencies]</c> into <paramref name="resolved" />, skipping any package
    ///     already present there — either because another dependent already resolved it (a diamond) or because it is
    ///     still being resolved higher up this very call stack (a cycle in the declared graph, which is otherwise
    ///     nothing to reject: nothing requires the packages a build's own files actually import to form one).
    /// </summary>
    /// <param name="isEntry">
    ///     Whether these are the dependencies of the project being built. Only its <c>dev = true</c> dependencies are
    ///     resolved: a package's development dependencies are what its own tests are written against, and a consumer
    ///     compiling the package needs no part of them. A package whose shipped source imports one is a package with
    ///     a mislabelled dependency, and the import says so.
    /// </param>
    private static void ResolveDependenciesOf(
        LoomConfig config,
        string describeOwner,
        bool isEntry,
        IReadOnlyDictionary<PackageName, string> packageDirectories,
        Dictionary<PackageName, LoomConfig> resolved,
        List<ConfigDiagnostic> diagnostics
    )
    {
        foreach (var (name, dependency) in config.Dependencies)
        {
            if (dependency.IsDevelopmentOnly && !isEntry)
                continue;

            if (resolved.ContainsKey(name))
                continue;

            if (!packageDirectories.TryGetValue(name, out var directory))
            {
                diagnostics.Add(new ConfigDiagnostic($"{describeOwner} depends on '{name}', but no directory was resolved for it."));
                continue;
            }

            // kept apart from the manifest-shaped problems below: this is the one a build hits by not having
            // installed its dependencies yet, where the answer is to run a package manager rather than fix a file.
            if (!Directory.Exists(directory))
            {
                diagnostics.Add(new ConfigDiagnostic($"{describeOwner} depends on '{name}', which is not installed in '{directory}'."));
                continue;
            }

            var packageConfig = ConfigReader.LocateFromDirectory(directory, out var configDiagnostics);
            if (packageConfig == null)
            {
                diagnostics.AddRange(
                    configDiagnostics.Count > 0
                        ? configDiagnostics
                        : [new ConfigDiagnostic($"'{name}' resolved to '{directory}', which has no {ConfigReader.ConfigFileName}.")]
                );

                continue;
            }

            var publishedName = packageConfig.Package?.Name;
            if (publishedName != name)
            {
                diagnostics.Add(
                    new ConfigDiagnostic(
                        $"'{name}' resolved to '{directory}', which publishes {(publishedName == null ? "no package" : $"'{publishedName}'")} instead."
                    )
                );

                continue;
            }

            resolved[name] = packageConfig;
            ResolveDependenciesOf(packageConfig, $"'{name}'", isEntry: false, packageDirectories, resolved, diagnostics);
        }
    }

    /// <summary>
    ///     Measures one root of the build against the lock: what its manifest asks for, and — for a dependency —
    ///     which version of it is actually installed.
    /// </summary>
    /// <remarks>
    ///     The entry project's own version is not compared: the lock covers what the project resolved to, and a
    ///     project bumping its own <c>[package] version</c> has not changed a single answer in it.
    /// </remarks>
    private static void CheckAgainstLock(SourceRoot root, bool isEntry, LockFile lockFile, List<ConfigDiagnostic> diagnostics)
    {
        var describe = isEntry ? "the project" : $"'{root.Package?.Name}'";
        if (!lockFile.Satisfies(root.Config, isEntry, out var unmet))
            diagnostics.AddRange(unmet.Select(problem => new ConfigDiagnostic($"{LockFile.FileName} does not cover what {describe} depends on: {problem.Message}")));

        if (isEntry || root.Package is not { Name: { } name, Version: { } installed })
            return;

        var locked = lockFile.Find(name)?.Version;
        if (locked != null && !locked.Equals(installed))
        {
            diagnostics.Add(
                new ConfigDiagnostic($"'{name}' is installed at {installed}, but {LockFile.FileName} locks {locked}.")
            );
        }
    }
}
