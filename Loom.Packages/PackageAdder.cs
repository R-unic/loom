using Loom.Config;

namespace Loom.Packages;

/// <summary>One package as adding it left it: the requirement written into the manifest, and the version that met it.</summary>
public sealed record AddedPackage(PackageName Name, VersionRequirement Requirement, Version Version, bool IsDevelopmentOnly)
{
    public override string ToString() => $"{Name} {Requirement} ({Version})";
}

/// <summary>
///     Adds dependencies to a project: writes them into its manifest, then restores it. The half of a package
///     manager that changes what a project asks for, where <see cref="PackageManager" /> is the half that answers
///     what it already asks.
/// </summary>
/// <remarks>
///     A request naming no version is what makes this need an index at all: writing down what is published now is
///     the only way to answer it, and it is answered once, into the manifest, rather than on every build. The
///     manifest is written before resolution runs, since resolution reads the manifest — so a request that turns out
///     not to be satisfiable takes the manifest back to what it was, and a failed <c>add</c> leaves a project
///     exactly as it found it.
/// </remarks>
public static class PackageAdder
{
    /// <summary>
    ///     Adds every package in <paramref name="requests" /> to <paramref name="project" /> and restores it,
    ///     answering what was added, or <see langword="null" /> with the <paramref name="diagnostics" /> saying what
    ///     stopped it.
    /// </summary>
    /// <remarks>
    ///     A package already depended upon has its requirement replaced rather than declared twice: asking for a
    ///     version of something the project already uses is asking to move to it.
    /// </remarks>
    public static IReadOnlyList<AddedPackage>? Add(
        LoomConfig project,
        IReadOnlyList<PackageRequest> requests,
        out IReadOnlyList<ConfigDiagnostic> diagnostics
    )
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;
        if (requests.Count == 0)
        {
            reported.Add(new ConfigDiagnostic("name at least one package to add."));
            return null;
        }

        if (!Validate(project, requests, reported))
            return null;

        var index = PackageIndexes.Open(project, out var indexDiagnostics);
        if (index == null)
        {
            reported.AddRange(indexDiagnostics);
            return null;
        }

        var entries = new List<(PackageRequest Request, VersionRequirement Requirement)>();
        foreach (var request in requests)
        {
            var requirement = RequirementFor(request, index, reported);
            if (requirement == null)
                return null;

            entries.Add((request, requirement));
        }

        var manifestPath = Path.Combine(project.ProjectDirectory, ConfigReader.ConfigFileName);
        string original;
        string manifest;
        try
        {
            original = manifest = File.ReadAllText(manifestPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reported.Add(new ConfigDiagnostic($"could not read '{manifestPath}': {exception.Message}"));
            return null;
        }

        foreach (var (request, requirement) in entries)
        {
            var edited = ManifestEditor.WithDependency(manifest, request.Name, requirement, request.IsDevelopmentOnly, out var editDiagnostics);
            if (edited == null)
            {
                reported.AddRange(editDiagnostics);
                return null;
            }

            manifest = edited;
        }

        if (!TryWrite(manifestPath, manifest, reported))
            return null;

        var added = Restore(project, manifestPath, entries, reported);
        if (added == null)
            Revert(manifestPath, original, reported);

        return added;
    }

    /// <summary>
    ///     What can be said about the requests before an index is opened: a project cannot depend on itself, and one
    ///     command cannot ask for two versions of one package.
    /// </summary>
    private static bool Validate(LoomConfig project, IReadOnlyList<PackageRequest> requests, List<ConfigDiagnostic> reported)
    {
        var named = new HashSet<PackageName>();
        foreach (var request in requests)
        {
            if (project.Package?.Name == request.Name)
                reported.Add(new ConfigDiagnostic($"'{request.Name}' is this project, and a package cannot depend on itself."));
            else if (!named.Add(request.Name))
                reported.Add(new ConfigDiagnostic($"'{request.Name}' is named more than once."));
        }

        return reported.Count == 0;
    }

    /// <summary>
    ///     The requirement to write down: the one asked for, once the index confirms something satisfies it, and
    ///     otherwise compatibility with the newest version published — a released one when there is one, since a
    ///     request with no opinion is not a request for a pre-release.
    /// </summary>
    private static VersionRequirement? RequirementFor(PackageRequest request, IPackageIndex index, List<ConfigDiagnostic> reported)
    {
        var publications = index.Publications(request.Name, out var indexDiagnostics);
        if (indexDiagnostics.Count > 0)
        {
            reported.AddRange(indexDiagnostics);
            return null;
        }

        if (publications.Count == 0)
        {
            reported.Add(new ConfigDiagnostic($"'{request.Name}' is not published in '{index.Description}'."));
            return null;
        }

        if (request.Requirement is { } requirement)
        {
            if (publications.Any(publication => requirement.Satisfies(publication.Version)))
                return requirement;

            reported.Add(
                new ConfigDiagnostic(
                    $"no published version of '{request.Name}' satisfies '{requirement}'; "
                    + $"'{index.Description}' publishes {string.Join(", ", publications.Select(publication => publication.Version))}."
                )
            );

            return null;
        }

        var newest = publications.LastOrDefault(publication => !publication.Version.IsPrerelease) ?? publications[^1];
        return VersionRequirement.Parse($"^{newest.Version}");
    }

    /// <summary>
    ///     Restores the project as the edited manifest now describes it, and reads back what that made of each
    ///     request. The manifest is re-read from disk rather than amended in memory: what the next build will resolve
    ///     is the file, and reading it back is what proves the edit says what it was meant to.
    /// </summary>
    private static List<AddedPackage>? Restore(
        LoomConfig project,
        string manifestPath,
        List<(PackageRequest Request, VersionRequirement Requirement)> entries,
        List<ConfigDiagnostic> reported
    )
    {
        var updated = ConfigReader.LocateFromDirectory(project.ProjectDirectory, out var configDiagnostics);
        if (updated == null)
        {
            reported.Add(new ConfigDiagnostic($"'{manifestPath}' could not be read back after adding to it."));
            reported.AddRange(configDiagnostics);
            return null;
        }

        if (!PackageManager.Restore(updated, out var restoreDiagnostics))
        {
            reported.AddRange(restoreDiagnostics);
            return null;
        }

        var lockFile = LockFileReader.LocateFromDirectory(updated.ProjectDirectory, out var lockDiagnostics);
        if (lockFile == null)
        {
            reported.AddRange(lockDiagnostics);
            reported.Add(new ConfigDiagnostic($"the project was restored without writing {LockFile.FileName}."));
            return null;
        }

        var added = new List<AddedPackage>();
        foreach (var (request, requirement) in entries)
        {
            var locked = lockFile.Find(request.Name);
            if (locked == null)
            {
                reported.Add(new ConfigDiagnostic($"'{request.Name}' was added to the manifest but is not in {LockFile.FileName}."));
                return null;
            }

            added.Add(new AddedPackage(request.Name, requirement, locked.Version, request.IsDevelopmentOnly));
        }

        return added;
    }

    private static bool TryWrite(string manifestPath, string manifest, List<ConfigDiagnostic> reported)
    {
        try
        {
            File.WriteAllText(manifestPath, manifest);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reported.Add(new ConfigDiagnostic($"could not write '{manifestPath}': {exception.Message}"));
            return false;
        }
    }

    /// <summary>
    ///     Puts the manifest back as it was after an add that could not be completed. A lock file written on the way
    ///     is left alone: a lock naming a package nothing depends on any more is something to prune rather than a
    ///     project in a state it cannot build from.
    /// </summary>
    private static void Revert(string manifestPath, string original, List<ConfigDiagnostic> reported)
    {
        try
        {
            File.WriteAllText(manifestPath, original);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reported.Add(new ConfigDiagnostic($"could not restore '{manifestPath}' after a failed add: {exception.Message}"));
        }
    }
}
