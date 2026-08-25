using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     Publishes a project as one version of a package: works out what it is made of, then hands that to an index.
/// </summary>
/// <remarks>
///     The two halves are separate calls because they answer to different people. What a package consists of is a
///     property of the project — its manifest, its sources, and the files a reader of the package needs — and can be
///     shown to its author before anything leaves the machine. Where a version goes, and whether it may go there at
///     all, belongs to the index: a local directory takes a copy, and a registry will take an upload, without
///     changing what was prepared.
/// </remarks>
public static class PackagePublisher
{
    /// <summary>
    ///     Files beside the manifest that are published with it. Not source, but the part of a package a person
    ///     reads, and no use to them in a registry that dropped it.
    /// </summary>
    private static readonly string[] _includedPrefixes = ["README", "LICENSE", "LICENCE", "CHANGELOG"];

    /// <summary>
    ///     What publishing <paramref name="project" /> would send, or <see langword="null" /> with the
    ///     <paramref name="diagnostics" /> saying why it cannot be published: no identity to publish under, or no
    ///     source to publish.
    /// </summary>
    /// <remarks>
    ///     The output directory and the installed packages are deliberately not part of it. Compiled output is
    ///     consumer-specific — it names the entry project's runtime — and a dependency's own dependencies are resolved
    ///     by whoever consumes it, from the requirements the manifest states, so shipping either would ship a copy of
    ///     one consumer's build to every other one. A lock file is left behind for the same reason: it records what
    ///     this project resolved to, which is not what a project depending on it will resolve to.
    /// </remarks>
    public static PackagePayload? Prepare(LoomConfig project, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        if (project.Package is not { Name: { } name, Version: { } version })
        {
            reported.Add(
                new ConfigDiagnostic(
                    $"the project has no [package] table, so it has no identity to publish under; give it a 'name' and a 'version' in {ConfigReader.ConfigFileName}."
                )
            );

            return null;
        }

        if (!Directory.Exists(project.Files.SourceDirectory))
        {
            reported.Add(new ConfigDiagnostic($"the source directory '{project.Files.SourceDirectory}' does not exist, so there is nothing to publish."));
            return null;
        }

        var files = new List<string> { ConfigReader.ConfigFileName };
        files.AddRange(SourceFiles(project));
        if (!files.Any(file => file.EndsWith(".loom", StringComparison.OrdinalIgnoreCase)))
        {
            reported.Add(new ConfigDiagnostic($"'{project.Files.SourceDirectory}' holds no .loom files, so there is nothing to publish."));
            return null;
        }

        files.AddRange(ReadableFiles(project));
        return new PackagePayload(name, version, project.ProjectDirectory, files);
    }

    /// <summary>
    ///     Whether <paramref name="index" /> would take <paramref name="payload" /> at all. Asked by
    ///     <see cref="Publish" />, and worth asking before whatever a caller does to satisfy itself that the version
    ///     is fit to publish: the answer does not depend on any of it, and a version that is spoken for is spoken for
    ///     however good the one being offered is.
    /// </summary>
    public static bool CanPublish(PackagePayload payload, IPackageIndex index, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (!index.Publications(payload.Name).Any(publication => publication.Version.Equals(payload.Version)))
            return true;

        diagnostics =
        [
            new ConfigDiagnostic(
                $"'{payload.Name}' {payload.Version} is already published in '{index.Description}'; a published version is never replaced, so publish a new one."
            )
        ];

        return false;
    }

    /// <summary>
    ///     Publishes <paramref name="payload" /> to <paramref name="index" />, answering whether it is now published
    ///     there.
    /// </summary>
    public static bool Publish(PackagePayload payload, IPackageIndex index, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        CanPublish(payload, index, out diagnostics) && index.Publish(payload, out diagnostics);

    /// <summary>Every file under the source directory, as a path relative to the project.</summary>
    private static IEnumerable<string> SourceFiles(LoomConfig project) =>
        Directory.EnumerateFiles(project.Files.SourceDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(project.ProjectDirectory, file))
            .Order(StringComparer.Ordinal);

    /// <summary>The files at the top of the project that are published because a reader of the package wants them.</summary>
    private static IEnumerable<string> ReadableFiles(LoomConfig project) =>
        Directory.EnumerateFiles(project.ProjectDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(file => _includedPrefixes.Any(prefix => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal);
}
