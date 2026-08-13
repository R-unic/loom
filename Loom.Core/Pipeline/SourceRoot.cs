using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

/// <summary>
///     One project inside a compilation unit: the files under its source directory, together with the
///     <see cref="LoomConfig" /> governing them. A unit has one root per project it compiles — the entry
///     project, plus one for every dependency distributed as source — and each root keeps its own project
///     directory, output directory and package identity rather than borrowing the entry project's.
/// </summary>
public sealed class SourceRoot(LoomConfig config, IEnumerable<SourceFile>? files = null)
{
    public LoomConfig Config { get; } = config;

    /// <summary>
    ///     Every <c>.loom</c> file under <see cref="SourceDirectory" />, or the files <paramref name="files" />
    ///     supplied. Mutable because hosts without a real file system — the intrinsic bootstrap, the playground
    ///     — hand the root its files instead of having them loaded from disk; passing them here means the
    ///     directory is never read at all, which is what a host that has no such directory needs.
    /// </summary>
    public List<SourceFile> Files { get; } = files?.ToList() ?? FileManager.LoadDirectory(config.Files.SourceDirectory);

    /// <summary>The identity this root is published under; <see langword="null" /> for a project that is never published, such as a game.</summary>
    public PackageConfig? Package => Config.Package;

    /// <summary>
    ///     This root's source directory, absolute. Read off the config on every access rather than captured at
    ///     construction, so a config edited after the unit was built is not silently compiled against the
    ///     directory the config used to name.
    /// </summary>
    public string SourceDirectory => Path.GetFullPath(Config.Files.SourceDirectory);

    /// <summary>
    ///     Whether <paramref name="absolutePath" /> names this root's source directory or something under it.
    ///     Which root a file <em>belongs</em> to is <see cref="SourceRootSet.Of" />'s answer to give, though:
    ///     roots nest, and then more than one of them contains the file.
    /// </summary>
    public bool Contains(string absolutePath) => PathComparison.IsUnder(absolutePath, SourceDirectory);

    /// <summary>
    ///     Which realm <paramref name="absolutePath" /> runs in, by the <c>[realms]</c> directory naming it.
    ///     <see cref="Realm.Shared" /> when none does, so a project that declares no realms has one realm and
    ///     no boundary anything can cross.
    /// </summary>
    /// <remarks>
    ///     The longest directory wins, so a realm declared inside another narrows it rather than being
    ///     shadowed by it: <c>net</c> may be shared while <c>net/server</c> is not.
    /// </remarks>
    public Realm RealmOf(string absolutePath)
    {
        var realm = Realm.Shared;
        var matched = -1;
        foreach (var (directory, declared) in Config.Realms)
        {
            // The manifest spells a nested directory with '/' whatever the platform, which Path.Combine
            // keeps verbatim - so on a system that separates with '\' the combined path matches nothing.
            var full = Path.Combine(SourceDirectory, directory.Replace('/', Path.DirectorySeparatorChar));
            if (directory.Length <= matched || !PathComparison.IsUnder(absolutePath, full))
                continue;

            realm = declared;
            matched = directory.Length;
        }

        return realm;
    }

    /// <summary>
    ///     Names <paramref name="file" /> the way a diagnostic should: relative to this root, qualified by the
    ///     package the root publishes so that a file of a dependency is not mistaken for one of the reader's own.
    /// </summary>
    public string Describe(SourceFile file)
    {
        var relativePath = file.RelativePath(Config.Files.SourceDirectory);
        return Package?.Name is { } packageName ? $"{packageName}/{relativePath}" : relativePath;
    }

    public override string ToString() => Package?.Name?.ToString() ?? Config.ProjectDirectory;
}
