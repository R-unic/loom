using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     An index that is a directory on disk: <c>&lt;index&gt;/&lt;scope&gt;/&lt;name&gt;/&lt;version&gt;</c>, each
///     version directory a Loom project of its own. What it publishes is therefore stated by the same
///     <c>loom-config.toml</c> the compiler reads, and installing is a copy.
/// </summary>
/// <remarks>
///     This is the whole of an offline registry — a vendored directory, a checkout of a monorepo, the fixtures a
///     test resolves against — and the shape a network index has to answer like. Nothing here caches: a directory
///     read is cheap next to what resolution does with the answer, and a stale cache of what is published is
///     exactly the bug an index must not have.
/// </remarks>
/// <param name="rootDirectory">The directory the index is in.</param>
/// <param name="source">
///     What a lock file should record as where a version came from, which is the index as the manifest spells it
///     rather than where that resolved to on this machine — a lock is committed, and an absolute path is the one
///     thing in it certain not to mean the same thing twice. <see langword="null" /> records no source at all.
/// </param>
public sealed class LocalPackageIndex(string rootDirectory, string? source = null) : IPackageIndex
{
    private readonly string _root = Path.GetFullPath(rootDirectory);

    public string Description => _root;

    /// <remarks>
    ///     Nothing is ever reported through <paramref name="diagnostics" />: a directory that is not there is a
    ///     package the index does not publish, and there is no state between asking and being answered for anything
    ///     else to go wrong in.
    /// </remarks>
    public IReadOnlyList<PublishedPackage> Publications(PackageName package, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var directory = Path.Combine(_root, package.Scope ?? string.Empty, package.Name);
        if (!Directory.Exists(directory))
            return [];

        var published = new List<PublishedPackage>();
        foreach (var versionDirectory in Directory.EnumerateDirectories(directory))
        {
            // a directory whose name is not a version, or that holds no manifest, is not a publication - an index
            // may well hold a README beside the versions, and a build has nothing to say about that
            if (!Version.TryParse(Path.GetFileName(versionDirectory), out var version))
                continue;

            var config = ConfigReader.LocateFromDirectory(versionDirectory, out _);
            if (config?.Package?.Name != package)
                continue;

            // the directory names the version, and a manifest disagreeing with it makes the index unanswerable:
            // which of the two a dependent asked for could not be said
            if (config.Package.Version != version)
                continue;

            published.Add(new PublishedPackage(package, version, config.Dependencies.Values, source: source));
        }

        published.Sort((left, right) => left.Version.CompareTo(right.Version));
        return published;
    }

    public bool 
        Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var packageSource = Path.Combine(_root, package.Name.Scope ?? string.Empty, package.Name.Name, package.Version.ToString());
        if (!Directory.Exists(packageSource))
        {
            diagnostics = [new ConfigDiagnostic($"'{package}' is not published in '{_root}'.")];
            return false;
        }

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);

            CopyDirectory(packageSource, directory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [new ConfigDiagnostic($"could not install '{package}' into '{directory}': {exception.Message}")];
            return false;
        }
    }

    /// <summary>
    ///     Publishes into <c>&lt;index&gt;/&lt;scope&gt;/&lt;name&gt;/&lt;version&gt;</c>, which is the directory
    ///     <see cref="Publications" /> reads back — so publishing here is exactly the shape a version already in the
    ///     index has, whether it was published by this or written by hand.
    /// </summary>
    /// <remarks>
    ///     The version directory is created only when the copy is about to happen and is removed again if part of it
    ///     fails: half a package in an index reads as a published version, and a consumer resolving it would compile
    ///     against files that were never published.
    /// </remarks>
    public bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var directory = Path.Combine(_root, payload.Name.Scope ?? string.Empty, payload.Name.Name, payload.Version.ToString());
        if (Directory.Exists(directory))
        {
            diagnostics = [new ConfigDiagnostic($"'{directory}' already exists, so '{payload}' cannot be published into it.")];
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in payload.Files)
            {
                var destination = Path.Combine(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(payload.Root, file), destination, true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(directory);
            diagnostics = [new ConfigDiagnostic($"could not publish '{payload}' into '{directory}': {exception.Message}")];
            return false;
        }
    }

    /// <summary>Removes a version directory that was not published in full, leaving the index as it was.</summary>
    private static void Discard(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // nothing further can be done about it here, and the failure being reported is the one worth reporting
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
