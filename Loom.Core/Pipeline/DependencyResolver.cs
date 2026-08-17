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
public static class DependencyResolver
{
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
        ResolveDependenciesOf(entry, "the project", packageDirectories, resolved, reported);

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
    private static void ResolveDependenciesOf(
        LoomConfig config,
        string describeOwner,
        IReadOnlyDictionary<PackageName, string> packageDirectories,
        Dictionary<PackageName, LoomConfig> resolved,
        List<ConfigDiagnostic> diagnostics
    )
    {
        foreach (var name in config.Dependencies.Keys)
        {
            if (resolved.ContainsKey(name))
                continue;

            if (!packageDirectories.TryGetValue(name, out var directory))
            {
                diagnostics.Add(new ConfigDiagnostic($"{describeOwner} depends on '{name}', but no directory was resolved for it."));
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
            ResolveDependenciesOf(packageConfig, $"'{name}'", packageDirectories, resolved, diagnostics);
        }
    }
}
