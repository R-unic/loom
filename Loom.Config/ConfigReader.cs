using Tomlyn;
using Tomlyn.Model;

namespace Loom.Config;

public static class ConfigReader
{
    public const string ConfigFileName = "loom-config.toml";
    private const int EditionLength = 4;
    private const string VersionKey = "version";
    private const string DevelopmentKey = "dev";

    public static LoomConfig? LocateFromDirectory(string directoryPath) => LocateFromDirectory(directoryPath, out _);

    /// <summary>
    ///     Reads the manifest of the project in <paramref name="directoryPath" />. A malformed manifest yields
    ///     <see langword="null" /> alongside the <paramref name="diagnostics" /> explaining why, never an exception; a
    ///     directory with no manifest at all yields <see langword="null" /> and no diagnostics, since that is not an error
    ///     here.
    /// </summary>
    public static LoomConfig? LocateFromDirectory(string directoryPath, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (string.IsNullOrEmpty(directoryPath))
            return null;

        if (!Directory.Exists(directoryPath))
            return null;

        var configPath = Path.Combine(directoryPath, ConfigFileName);
        if (!File.Exists(configPath))
            return null;

        var config = ReadFile(configPath, out diagnostics);
        if (config == null)
            return null;

        config.ProjectDirectory = directoryPath;
        return config;
    }

    private static LoomConfig? ReadFile(string path, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        LoomConfig? config;
        try
        {
            config = TomlSerializer.Deserialize(File.ReadAllText(path), LoomConfigContext.Default.LoomConfig);
        }
        catch (TomlException exception)
        {
            reported.AddRange(exception.Diagnostics.Select(ConfigDiagnostic.FromToml));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reported.Add(new ConfigDiagnostic($"could not read the Loom configuration file at '{path}': {exception.Message}"));
            return null;
        }

        if (config == null)
            return null;

        Validate(config, reported);
        if (reported.Count > 0)
            return null;

        config.ProjectDirectory = Path.GetDirectoryName(path)?.Trim() ?? "?";
        config.Files.SourceDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.SourceDirectory.TrimEnd('/', '\\').Trim();
        config.Files.OutputDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.OutputDirectory.TrimEnd('/', '\\').Trim();
        return config;
    }

    /// <summary>
    ///     Checks what Tomlyn's Native-AOT source generator cannot: required fields, the enum- and
    ///     identity-shaped fields a per-member <c>TomlConverter</c> would have read (see <see cref="LoomConfig.ProjectTypeEntry" />),
    ///     and the dependency table, which is read here.
    /// </summary>
    private static void Validate(LoomConfig config, List<ConfigDiagnostic> diagnostics)
    {
        ReadProjectType(config, diagnostics);

        if (config.Package is { } package)
            ReadPackage(package, diagnostics);

        ValidateDirectory(config.Files.SourceDirectory, "source_directory", diagnostics);
        ValidateDirectory(config.Files.OutputDirectory, "output_directory", diagnostics);
        ReadDependencies(config, diagnostics);
        ReadRealms(config, diagnostics);
        if (config.Registry != null && !IndexLocation.IsValid(config.Registry.Index))
            diagnostics.Add(new ConfigDiagnostic($"invalid registry index '{config.Registry.Index}'; {IndexLocation.Expected}."));
    }

    private static void ReadProjectType(LoomConfig config, List<ConfigDiagnostic> diagnostics)
    {
        if (config.ProjectTypeEntry == null)
        {
            config.ProjectType = ProjectType.Game;
            return;
        }

        switch (config.ProjectTypeEntry.Trim().ToLowerInvariant())
        {
            case "game":
                config.ProjectType = ProjectType.Game;
                break;
            case "library":
                config.ProjectType = ProjectType.Library;
                break;
            case "plugin":
                config.ProjectType = ProjectType.Plugin;
                break;
            default:
                diagnostics.Add(new ConfigDiagnostic($"unknown project type '{config.ProjectTypeEntry}'."));
                break;
        }
    }

    private static void ReadPackage(PackageConfig package, List<ConfigDiagnostic> diagnostics)
    {
        if (package.NameEntry == null)
            diagnostics.Add(new ConfigDiagnostic("[package] must specify a 'name'."));
        else if (PackageName.TryParse(package.NameEntry, out var name, out var nameError))
            package.Name = name;
        else
            diagnostics.Add(new ConfigDiagnostic(nameError));

        if (package.VersionEntry == null)
            diagnostics.Add(new ConfigDiagnostic("[package] must specify a 'version'."));
        else if (Version.TryParse(package.VersionEntry, out var version, out var versionError))
            package.Version = version;
        else
            diagnostics.Add(new ConfigDiagnostic(versionError));

        if (package.RealmEntry == null)
            package.Realm = Realm.Shared;
        else if (TryReadRealm(package.RealmEntry, out var realm))
            package.Realm = realm;
        else
            diagnostics.Add(new ConfigDiagnostic($"unknown realm '{package.RealmEntry}'; expected 'shared', 'client' or 'server'."));

        if (package.Edition != null && !IsEdition(package.Edition))
            diagnostics.Add(new ConfigDiagnostic($"invalid edition '{package.Edition}'; expected a four-digit year, e.g. \"2026\"."));
    }

    /// <summary>
    ///     Checks that a <c>[files]</c> directory can be what every later stage assumes it is: a path under the
    ///     project directory.
    /// </summary>
    /// <remarks>
    ///     Nothing downstream is in a position to complain about one that cannot. A source directory of
    ///     <c>""</c> reaches the pipeline as a path that throws the moment it is resolved, and a stage throwing
    ///     is the compiler-bug path — so a project misconfigured in one line is reported to its author as an
    ///     internal error asking them to file a bug. The manifest is where that is knowable, so it is where it
    ///     is said.
    /// </remarks>
    private static void ValidateDirectory(string directory, string key, List<ConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            diagnostics.Add(new ConfigDiagnostic($"[files] '{key}' cannot be empty."));
            return;
        }

        if (directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            diagnostics.Add(new ConfigDiagnostic($"[files] '{key}' contains characters that cannot appear in a path."));
            return;
        }

        // the project directory is prepended to whatever is written here, which an absolute path would leave
        // as neither the path written nor a path under the project
        if (Path.IsPathRooted(directory))
            diagnostics.Add(new ConfigDiagnostic($"[files] '{key}' must be relative to the project directory, but '{directory}' is absolute."));
    }

    /// <summary>
    ///     Reads <c>[realms]</c>, whose keys are directories under the source directory and whose values name
    ///     the realm the code in them runs in.
    /// </summary>
    /// <remarks>
    ///     Directories are normalised to one separator and stripped of leading and trailing ones, so the same
    ///     directory written <c>"net/server"</c>, <c>"net\server"</c> and <c>"/net/server/"</c> is one entry
    ///     rather than three that disagree. A directory listed twice is an error for the same reason a
    ///     dependency listed twice is: nothing here can say which line was meant.
    /// </remarks>
    private static void ReadRealms(LoomConfig config, List<ConfigDiagnostic> diagnostics)
    {
        foreach (var (directory, value) in config.RealmEntries)
        {
            if (value is not string name)
            {
                diagnostics.Add(new ConfigDiagnostic($"[realms] '{directory}' must name a realm: \"shared\", \"client\" or \"server\"."));
                continue;
            }

            if (!TryReadRealm(name, out var realm))
            {
                diagnostics.Add(new ConfigDiagnostic($"[realms] '{directory}' has invalid realm '{name}'; expected \"shared\", \"client\" or \"server\"."));
                continue;
            }

            // Normalised before it is checked, so what is validated is what is stored: a leading separator
            // reads as "the directory at the top of the source tree" rather than as an absolute path, while
            // something genuinely rooted - a drive letter - survives normalising and is still rejected.
            var normalized = NormalizeRealmDirectory(directory);
            if (string.IsNullOrWhiteSpace(normalized) || IsRooted(normalized) || normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                diagnostics.Add(new ConfigDiagnostic($"[realms] '{directory}' must be a non-empty path relative to the source directory."));
                continue;
            }

            if (!config.Realms.TryAdd(normalized, realm))
                diagnostics.Add(new ConfigDiagnostic($"[realms] lists '{directory}' more than once."));
        }
    }

    private static bool TryReadRealm(string name, out Realm realm)
    {
        realm = Realm.Shared;
        switch (name.Trim().ToLowerInvariant())
        {
            case "shared":
                realm = Realm.Shared;
                return true;
            case "client":
                realm = Realm.Client;
                return true;
            case "server":
                realm = Realm.Server;
                return true;
            default: return false;
        }
    }

    /// <summary>One spelling of a directory, so two ways of writing the same one are one entry.</summary>
    private static string NormalizeRealmDirectory(string directory) => directory.Replace('\\', '/').Trim().Trim('/');

    /// <summary>
    ///     Whether a normalized directory names somewhere absolute. Asked without <see cref="Path.IsPathRooted(string)" />,
    ///     which answers by the platform it is running on: a drive letter is rooted on Windows and an
    ///     ordinary directory name everywhere else, so the same manifest would be rejected on one machine
    ///     and read as a directory called <c>C:</c> on the next.
    /// </summary>
    /// <remarks>A leading separator is already gone by here, which is what leaves a drive letter to check for.</remarks>
    private static bool IsRooted(string normalized) => normalized is [_, ':', ..] && char.IsAsciiLetter(normalized[0]);

    private static void ReadDependencies(LoomConfig config, List<ConfigDiagnostic> diagnostics)
    {
        foreach (var (specifier, source) in config.DependencyEntries)
        {
            if (!PackageName.TryParse(specifier, out var name, out var error))
            {
                diagnostics.Add(new ConfigDiagnostic($"invalid dependency specifier '{specifier}': {error}"));
                continue;
            }

            var dependency = ReadDependency(name, source, diagnostics);
            if (dependency == null) continue;

            // specifiers differing only in case name the same package, so one of them has to go.
            if (!config.Dependencies.TryAdd(name, dependency))
                diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' is listed more than once."));
        }
    }

    private static Dependency? ReadDependency(PackageName name, object source, List<ConfigDiagnostic> diagnostics)
    {
        return source switch
        {
            string requirement => VersionRequirement.TryParse(requirement, out var parsed, out var requirementError)
                ? new Dependency(name, parsed)
                : reject(name, $"invalid version requirement '{requirement}': {requirementError}"),
            TomlTable table => ReadDependencyTable(name, table, diagnostics),
            _ => reject(name, $"must be a version requirement string or a table with a '{VersionKey}' key.")
        };

        Dependency? reject(PackageName dependencyName, string message)
        {
            diagnostics.Add(new ConfigDiagnostic($"dependency '{dependencyName}' {message}"));
            return null;
        }
    }

    private static Dependency? ReadDependencyTable(PackageName name, TomlTable table, List<ConfigDiagnostic> diagnostics)
    {
        var reported = diagnostics.Count;
        diagnostics.AddRange(
            table.Keys.Where(key => key is not (VersionKey or DevelopmentKey))
                .Select(key => new ConfigDiagnostic($"dependency '{name}' has an unknown key '{key}'; expected '{VersionKey}' or '{DevelopmentKey}'."))
        );

        var isDevelopmentOnly = false;
        if (table.TryGetValue(DevelopmentKey, out var development))
        {
            if (development is bool value)
                isDevelopmentOnly = value;
            else
                diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' has a non-boolean '{DevelopmentKey}'."));
        }

        VersionRequirement? version = null;
        if (!table.TryGetValue(VersionKey, out var requirement))
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' must specify a '{VersionKey}' requirement."));
        else if (requirement is not string text)
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' must have a version requirement written as a string, e.g. \"^1.2\"."));
        else if (!VersionRequirement.TryParse(text, out version, out var error))
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' has an invalid version requirement '{text}': {error}"));

        return version != null && diagnostics.Count == reported ? new Dependency(name, version, isDevelopmentOnly) : null;
    }

    private static bool IsEdition(string edition) => edition.Length == EditionLength && edition.All(char.IsAsciiDigit);

    /// <summary>
    ///     Whether an index is somewhere an index could be. A registry is reached over http, but an index is just as
    ///     legitimately a directory — vendored, checked out beside the project, or the fixtures a test resolves
    ///     against — so a path is accepted here and read relative to the project directory by whoever opens it.
    /// </summary>
    private static bool IsIndexLocation(string index)
    {
        if (Uri.TryCreate(index, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "file";

        return !string.IsNullOrWhiteSpace(index) && index.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }
}