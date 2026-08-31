using Loom.Config;
using Loom.Packages;

namespace Loom.Testing;

/// <summary>
///     An index that cannot answer anything: every question comes back with the reason instead of a result. It
///     stands for the registry a remote index cannot reach — the failure a directory on disk has no way of having,
///     and the one an empty result must never be read as.
/// </summary>
internal sealed class UnreachablePackageIndex(string description = "https://registry.test") : IPackageIndex
{
    public const string Reason = "could not reach 'https://registry.test': the connection timed out.";

    public string Description => description;

    public IReadOnlyList<PublishedPackage> Publications(PackageName package, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = Failure;
        return [];
    }

    public bool Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = Failure;
        return false;
    }

    public bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = Failure;
        return false;
    }

    private static IReadOnlyList<ConfigDiagnostic> Failure => [new ConfigDiagnostic(Reason)];
}
