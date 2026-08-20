namespace Loom.Config;

/// <summary>
///     One <c>[[package]]</c> entry of a <see cref="LockFile" />: a package and the single version resolution
///     landed on, alongside what a package manager needs to fetch that version again (<see cref="Source" />) and
///     prove it got the same bytes (<see cref="Checksum" />).
/// </summary>
/// <remarks>
///     Where the package lives on disk is deliberately absent. A lock file is committed and read again on another
///     machine, so a path written into it is the one thing certain not to survive the trip; deciding where a
///     resolved package lands is the package manager's job, and the compiler asks it for that directory
///     separately (see <c>DependencyResolver</c>).
/// </remarks>
public sealed class LockedPackage
{
    public LockedPackage(
        PackageName name,
        Version version,
        string? source = null,
        string? checksum = null,
        IEnumerable<PackageName>? dependencies = null
    )
    {
        Name = name;
        Version = version;
        Source = source;
        Checksum = checksum;
        Dependencies = dependencies?.Distinct().Order().ToArray() ?? [];
    }

    /// <summary>The package this entry locks.</summary>
    public PackageName Name { get; }

    /// <summary>The exact version every dependent in the build resolved to; never a requirement.</summary>
    public Version Version { get; }

    /// <summary>
    ///     Where the version came from, e.g. the index that published it, so a lock resolved against one registry
    ///     is not silently reused against another. <see langword="null" /> when the package manager records none.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    ///     Integrity of the fetched package, in whatever form the package manager writes it (e.g.
    ///     <c>"sha256:…"</c>). Opaque to the compiler, which hashes nothing and verifies nothing.
    /// </summary>
    public string? Checksum { get; }

    /// <summary>
    ///     The packages this one depends on, so the lock file is the resolved graph rather than a flat list — a
    ///     package manager can tell whether a lock still covers a build without reading every manifest in it.
    ///     Every name here is itself locked; <see cref="LockFileReader" /> rejects a lock where one is not.
    /// </summary>
    public IReadOnlyList<PackageName> Dependencies { get; }

    public override string ToString() => $"{Name} {Version}";
}
