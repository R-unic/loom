using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     What is published, and how to get it. The one thing resolution needs from the outside world, so that
///     choosing versions can be tested against a directory of fixtures rather than a network.
/// </summary>
public interface IPackageIndex
{
    /// <summary>Where this index is, as a diagnostic should name it.</summary>
    string Description { get; }

    /// <summary>
    ///     Every version of <paramref name="package" /> the index publishes, newest last, or empty when it publishes
    ///     no such package — which is a resolution failure to report rather than an index failure.
    /// </summary>
    IReadOnlyList<PublishedPackage> Publications(PackageName package);

    /// <summary>
    ///     Puts <paramref name="package" />'s files in <paramref name="directory" />, replacing whatever is there.
    ///     Fetching, unpacking or copying: which of those it is belongs to the index.
    /// </summary>
    bool Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics);

    /// <summary>
    ///     Publishes <paramref name="payload" />, so that <see cref="Publications" /> answers with it afterwards.
    ///     Copying into a directory or uploading to a registry: which of those it is belongs to the index, as with
    ///     <see cref="Install" />.
    /// </summary>
    /// <remarks>
    ///     An index that cannot be published to — one that is read-only, or a mirror — says so through the
    ///     <paramref name="diagnostics" /> rather than by throwing, the same way every other refusal here is
    ///     reported. Whether the version is already published is checked by <see cref="PackagePublisher" /> before
    ///     this is called, since the answer is the same for every index; an index enforcing it again is welcome to.
    /// </remarks>
    bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics);
}
