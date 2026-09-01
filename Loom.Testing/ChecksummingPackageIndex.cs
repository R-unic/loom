using Loom.Config;
using Loom.Packages;

namespace Loom.Testing;

/// <summary>
///     A local index that states a checksum for every version it publishes. Integrity is the other thing a directory
///     on disk has no way of stating — what is in the directory is what it is — so this stands in for a registry,
///     whose index endpoint states one per version.
/// </summary>
/// <param name="checksum">What the index states for every version, whatever is actually in the directory.</param>
internal sealed class ChecksummingPackageIndex(string rootDirectory, string checksum) : IPackageIndex
{
    private readonly LocalPackageIndex _index = new(rootDirectory);

    public string Description => _index.Description;

    public IReadOnlyList<PublishedPackage> Publications(PackageName package, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        _index.Publications(package, out diagnostics)
            .Select(publication => new PublishedPackage(publication.Name, publication.Version, publication.Dependencies, checksum, publication.Source))
            .ToArray();

    public bool Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics) =>
        _index.Install(package, directory, out diagnostics);

    public bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics) => _index.Publish(payload, out diagnostics);
}
