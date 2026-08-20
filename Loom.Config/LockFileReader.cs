using Tomlyn;

namespace Loom.Config;

/// <summary>
///     Reads <c>loom-lock.toml</c>. Like <see cref="ConfigReader" />, a malformed file comes back as
///     <see langword="null" /> plus the <see cref="ConfigDiagnostic" />s explaining why, never an exception: a lock
///     file is written by a tool and edited by hand anyway, and a build has to be able to say what is wrong with it.
/// </summary>
public static class LockFileReader
{
    private const string NameKey = "name";
    private const string VersionKey = "version";
    private const string SourceKey = "source";
    private const string ChecksumKey = "checksum";
    private const string DependenciesKey = "dependencies";

    public static LockFile? LocateFromDirectory(string directoryPath) => LocateFromDirectory(directoryPath, out _);

    /// <summary>
    ///     Reads the lock file of the project in <paramref name="directoryPath" />. A project with no lock file
    ///     yields <see langword="null" /> and no diagnostics — it has simply never been resolved — which is the
    ///     same shape <see cref="ConfigReader.LocateFromDirectory(string, out IReadOnlyList{ConfigDiagnostic})" />
    ///     answers with for a directory holding no manifest.
    /// </summary>
    public static LockFile? LocateFromDirectory(string directoryPath, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return null;

        var path = Path.Combine(directoryPath, LockFile.FileName);
        if (!File.Exists(path))
            return null;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [new ConfigDiagnostic($"could not read the lock file at '{path}': {exception.Message}")];
            return null;
        }

        return Read(text, out diagnostics);
    }

    /// <summary>Reads a lock file from its text, for a caller already holding it.</summary>
    public static LockFile? Read(string text, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        LockFileDocument? document;
        try
        {
            document = TomlSerializer.Deserialize(text, LockFileContext.Default.LockFileDocument);
        }
        catch (TomlException exception)
        {
            reported.AddRange(exception.Diagnostics.Select(ConfigDiagnostic.FromToml));
            return null;
        }

        if (document == null)
            return null;

        ReadFormatVersion(document, reported);
        var packages = ReadPackages(document, reported);
        return reported.Count == 0 ? new LockFile(packages.Values) : null;
    }

    /// <summary>
    ///     A lock file names the format it was written in, and a version this compiler does not know is refused
    ///     rather than read as far as it parses: the point of the file is that what it says is exactly what was
    ///     resolved, so half-understanding one is worse than resolving again.
    /// </summary>
    private static void ReadFormatVersion(LockFileDocument document, List<ConfigDiagnostic> diagnostics)
    {
        switch (document.FormatVersion)
        {
            case null:
                diagnostics.Add(new ConfigDiagnostic($"the lock file must specify a 'version'; this compiler writes version {LockFile.CurrentFormatVersion}."));
                break;
            case not LockFile.CurrentFormatVersion:
                diagnostics.Add(
                    new ConfigDiagnostic(
                        $"lock file version {document.FormatVersion} cannot be read; this compiler writes version {LockFile.CurrentFormatVersion}."
                    )
                );

                break;
        }
    }

    /// <summary>
    ///     Reads every <c>[[package]]</c> entry, then checks that the lock is closed: a package it says something
    ///     depends on has to be locked itself, since a graph with a name missing from it answers nothing about the
    ///     build it claims to describe.
    /// </summary>
    private static Dictionary<PackageName, LockedPackage> ReadPackages(LockFileDocument document, List<ConfigDiagnostic> diagnostics)
    {
        var packages = new Dictionary<PackageName, LockedPackage>();
        foreach (var entry in document.Packages)
        {
            var package = ReadPackage(entry, diagnostics);
            if (package == null)
                continue;

            // two entries naming one package leave nothing here able to say which version the build uses.
            if (!packages.TryAdd(package.Name, package))
                diagnostics.Add(new ConfigDiagnostic($"'{package.Name}' is locked more than once."));
        }

        foreach (var package in packages.Values)
        {
            diagnostics.AddRange(
                package.Dependencies.Where(dependency => !packages.ContainsKey(dependency))
                    .Select(dependency => new ConfigDiagnostic($"'{package.Name}' depends on '{dependency}', which the lock file does not lock."))
            );
        }

        return packages;
    }

    private static LockedPackage? ReadPackage(Dictionary<string, object> entry, List<ConfigDiagnostic> diagnostics)
    {
        var reported = diagnostics.Count;

        var name = ReadName(entry, diagnostics);
        var describe = name == null ? "[[package]]" : $"'{name}'";
        var version = ReadVersion(entry, describe, diagnostics);
        var source = ReadText(entry, SourceKey, describe, diagnostics);
        var checksum = ReadText(entry, ChecksumKey, describe, diagnostics);
        var dependencies = ReadDependencies(entry, describe, diagnostics);

        if (source != null && !IndexLocation.IsValid(source))
            diagnostics.Add(new ConfigDiagnostic($"{describe} has an invalid '{SourceKey}' '{source}'; {IndexLocation.Expected}."));

        // a package manager writing nothing is how "no checksum" is said; writing an empty one says the file
        // records an integrity check it does not have.
        if (checksum != null && string.IsNullOrWhiteSpace(checksum))
            diagnostics.Add(new ConfigDiagnostic($"{describe} has an empty '{ChecksumKey}'; omit the key instead."));

        var known = $"'{NameKey}', '{VersionKey}', '{SourceKey}', '{ChecksumKey}' or '{DependenciesKey}'";
        diagnostics.AddRange(
            entry.Keys.Where(key => key is not (NameKey or VersionKey or SourceKey or ChecksumKey or DependenciesKey))
                .Select(key => new ConfigDiagnostic($"{describe} has an unknown key '{key}'; expected {known}."))
        );

        return name != null && version != null && diagnostics.Count == reported
            ? new LockedPackage(name, version, source, checksum, dependencies)
            : null;
    }

    private static PackageName? ReadName(Dictionary<string, object> entry, List<ConfigDiagnostic> diagnostics)
    {
        if (!entry.TryGetValue(NameKey, out var value))
        {
            diagnostics.Add(new ConfigDiagnostic($"[[package]] must specify a '{NameKey}'."));
            return null;
        }

        if (value is not string text)
        {
            diagnostics.Add(new ConfigDiagnostic($"[[package]] must have a '{NameKey}' written as a string, e.g. \"scope/name\"."));
            return null;
        }

        if (PackageName.TryParse(text, out var name, out var error))
            return name;

        diagnostics.Add(new ConfigDiagnostic($"[[package]] has an invalid '{NameKey}' '{text}': {error}"));
        return null;
    }

    /// <summary>
    ///     Reads the one version the entry locks. A requirement is refused here rather than read as the version it
    ///     resembles: a lock file answering with a range would answer nothing a manifest does not already say.
    /// </summary>
    private static Version? ReadVersion(Dictionary<string, object> entry, string describe, List<ConfigDiagnostic> diagnostics)
    {
        if (!entry.TryGetValue(VersionKey, out var value))
        {
            diagnostics.Add(new ConfigDiagnostic($"{describe} must specify a '{VersionKey}'."));
            return null;
        }

        if (value is not string text)
        {
            diagnostics.Add(new ConfigDiagnostic($"{describe} must have a '{VersionKey}' written as a string, e.g. \"1.2.3\"."));
            return null;
        }

        if (Version.TryParse(text, out var version, out var error))
            return version;

        diagnostics.Add(new ConfigDiagnostic($"{describe} has an invalid '{VersionKey}' '{text}': {error}"));
        return null;
    }

    private static string? ReadText(Dictionary<string, object> entry, string key, string describe, List<ConfigDiagnostic> diagnostics)
    {
        if (!entry.TryGetValue(key, out var value))
            return null;

        if (value is string text)
            return text;

        diagnostics.Add(new ConfigDiagnostic($"{describe} must have a '{key}' written as a string."));
        return null;
    }

    private static List<PackageName> ReadDependencies(Dictionary<string, object> entry, string describe, List<ConfigDiagnostic> diagnostics)
    {
        var dependencies = new List<PackageName>();
        if (!entry.TryGetValue(DependenciesKey, out var value))
            return dependencies;

        if (value is not IEnumerable<object> items)
        {
            diagnostics.Add(new ConfigDiagnostic($"{describe} must have '{DependenciesKey}' written as an array of package names."));
            return dependencies;
        }

        foreach (var item in items)
        {
            if (item is not string text)
                diagnostics.Add(new ConfigDiagnostic($"{describe} lists a dependency that is not a package name."));
            else if (PackageName.TryParse(text, out var dependency, out var error))
                dependencies.Add(dependency);
            else
                diagnostics.Add(new ConfigDiagnostic($"{describe} depends on '{text}', which is not a package name: {error}"));
        }

        return dependencies;
    }

}
