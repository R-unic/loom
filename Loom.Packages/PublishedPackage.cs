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
public sealed class PublishedPackage(PackageName name, Version version, IEnumerable<Dependency> dependencies, string? checksum = null, string? source = null)
{
    public PackageName Name { get; } = name;

    public Version Version { get; } = version;

    /// <summary>What this version needs to compile, keyed by package.</summary>
    public IReadOnlyList<Dependency> Dependencies { get; } = dependencies.Where(dependency => !dependency.IsDevelopmentOnly).ToArray();

    /// <summary>Integrity of the published files, in whatever form the index states it; recorded in the lock as-is.</summary>
    public string? Checksum { get; } = checksum;

    /// <summary>Where the version came from, recorded in the lock so a lock resolved against one index is not reused against another.</summary>
    public string? Source { get; } = source;

    /// <summary>This version as the lock file records it, once resolution has chosen it.</summary>
    public LockedPackage ToLockedPackage() =>
        new(Name, Version, Source, Checksum, Dependencies.Select(dependency => dependency.Name));

    public override string ToString() => $"{Name} {Version}";
}
