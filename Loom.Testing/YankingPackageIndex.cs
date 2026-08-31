using Loom.Config;
using Loom.Packages;

namespace Loom.Testing;

/// <summary>
///     A local index with versions withdrawn from it. Yanking is the one thing a directory on disk cannot state —
///     what is in the directory is what it stands behind — so this stands in for the registry's index endpoint,
///     which states it per version.
/// </summary>
/// <param name="yanked">The withdrawn versions, each written <c>name@version</c>.</param>
internal sealed class YankingPackageIndex(string rootDirectory, params string[] yanked) : IPackageIndex
{
    private readonly LocalPackageIndex _index = new(rootDirectory);
    private readonly HashSet<string> _yanked = [..yanked];

    public string Description => _index.Description;

    public IReadOnlyList<PublishedPackage> Publications(PackageName package, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        _index.Publications(package, out diagnostics).Select(Withdrawn).ToArray();

    public bool Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        _index.Install(package, directory, out diagnostics);

    public bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics) => _index.Publish(payload, out diagnostics);

    private PublishedPackage Withdrawn(PublishedPackage publication) =>
        _yanked.Contains($"{publication.Name}@{publication.Version}")
            ? new PublishedPackage(publication.Name, publication.Version, publication.Dependencies, publication.Checksum, publication.Source, yanked: true)
            : publication;
}
