using Loom.Config;

namespace Loom.Packages;

/// <summary>Opens the index a project resolves from, whatever its <c>[registry]</c> table points at.</summary>
public static class PackageIndexes
{
    /// <summary>
    ///     The index <paramref name="project" /> names, or <see langword="null" /> with the reason it could not be
    ///     opened. A relative path is read against the project directory, so a manifest naming an index beside the
    ///     project means the same thing wherever the build is run from.
    /// </summary>
    public static IPackageIndex? Open(LoomConfig project, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (project.Registry is not { } registry)
        {
            diagnostics = [new ConfigDiagnostic("the project has no [registry] index to resolve its dependencies from.")];
            return null;
        }

        if (Uri.TryCreate(registry.Index, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            diagnostics =
            [
                new ConfigDiagnostic($"resolving from a remote index ('{registry.Index}') is not supported yet; point [registry] index at a local directory.")
            ];

            return null;
        }

        var path = uri is { IsFile: true } ? uri.LocalPath : Path.Combine(project.ProjectDirectory, registry.Index);
        if (Directory.Exists(path))
            return new LocalPackageIndex(path, registry.Index);

        diagnostics = [new ConfigDiagnostic($"the index '{registry.Index}' is not a directory ('{Path.GetFullPath(path)}').")];
        return null;
    }
}
