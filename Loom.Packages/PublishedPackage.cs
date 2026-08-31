using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     One version of a package as an index publishes it: its identity, and what it says it depends on. What
///     resolution reads — it never opens the package's own files to find out, since an index is answerable
///     without fetching anything.
/// </summary>
/// <remarks>
///     <see cref="Dependencies" /> excludes the package's development-only ones. They are what its own tests are
///     written against and no part of compiling it for someone else, so they are not resolution's business either
///     — the same line <c>DependencyResolver</c> draws when it walks a build's roots.
/// </remarks>
public sealed class PublishedPackage(
    PackageName name,
    Version version,
    IEnumerable<Dependency> dependencies,
    string? checksum = null,
    string? source = null,
    bool yanked = false
)
{
    public PackageName Name { get; } = name;

    public Version Version { get; } = version;

    /// <summary>What this version needs to compile, keyed by package.</summary>
    public IReadOnlyList<Dependency> Dependencies { get; } = dependencies.Where(dependency => !dependency.IsDevelopmentOnly).ToArray();

    /// <summary>Integrity of the published files, in whatever form the index states it; recorded in the lock as-is.</summary>
    public string? Checksum { get; } = checksum;

    /// <summary>Where the version came from, recorded in the lock so a lock resolved against one index is not reused against another.</summary>
    public string? Source { get; } = source;

    /// <summary>
    ///     Whether the index has withdrawn this version from being chosen. The whole of a yank is that asymmetry:
    ///     resolution choosing anew passes over it, and a lock that already pins it installs it as before — a yank
    ///     says a version should stop being taken up, not that everyone already on it should stop building.
    /// </summary>
    /// <remarks>
    ///     An index with no way of withdrawing a version — a directory on disk — publishes nothing yanked, since
    ///     what is in the directory is what it stands behind.
    /// </remarks>
    public bool Yanked { get; } = yanked;

    /// <summary>This version as the lock file records it, once resolution has chosen it.</summary>
    public LockedPackage ToLockedPackage() =>
        new(Name, Version, Source, Checksum, Dependencies.Select(dependency => dependency.Name));

    /// <summary>
    ///     <paramref name="publications" /> as a diagnostic lists them, yanked versions marked. A version passed
    ///     over is worth saying was there: "nothing satisfies '^1.0'" beside a list holding 1.4.0 reads as a broken
    ///     resolver rather than as the yank it is.
    /// </summary>
    public static string Describe(IEnumerable<PublishedPackage> publications) =>
        string.Join(", ", publications.Select(publication => publication.Yanked ? $"{publication.Version} (yanked)" : publication.Version.ToString()));

    public override string ToString() => $"{Name} {Version}";
}
