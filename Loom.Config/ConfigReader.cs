using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace Loom.Config;

public static partial class ConfigReader
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
            config = TomlSerializer.Deserialize<LoomConfig>(File.ReadAllText(path));
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

        config.ProjectDirectory = Path.GetDirectoryName(path) ?? "?";
        config.Files.SourceDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.SourceDirectory.TrimEnd(Path.DirectorySeparatorChar);
        config.Files.OutputDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return config;
    }

    /// <summary>Checks what the TOML converters cannot: required fields, and the dependency table, which is read here.</summary>
    private static void Validate(LoomConfig config, List<ConfigDiagnostic> diagnostics)
    {
        if (config.Package is { } package)
        {
            if (package.Name == null)
                diagnostics.Add(new ConfigDiagnostic("[package] must specify a 'name'."));

            if (package.Version == null)
                diagnostics.Add(new ConfigDiagnostic("[package] must specify a 'version'."));

            if (package.Edition != null && !IsEdition(package.Edition))
                diagnostics.Add(new ConfigDiagnostic($"invalid edition '{package.Edition}'; expected a four-digit year, e.g. \"2026\"."));
        }

        ValidateDirectory(config.Files.SourceDirectory, "source_directory", diagnostics);
        ValidateDirectory(config.Files.OutputDirectory, "output_directory", diagnostics);

        ReadDependencies(config, diagnostics);
        if (config.Registry != null && !IsFetchableUrl(config.Registry.Index))
            diagnostics.Add(new ConfigDiagnostic($"invalid registry index '{config.Registry.Index}'; expected an http or https URL."));
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
            string requirement => IsVersionRequirement(requirement)
                ? new Dependency(name, requirement)
                : reject(name, $"invalid version requirement '{requirement}'; expected something like \"^1.2\" or \">=0.4.0\"."),
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

        if (!table.TryGetValue(VersionKey, out var requirement))
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' must specify a '{VersionKey}' requirement."));
        else if (requirement is not string text)
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' must have a version requirement written as a string, e.g. \"^1.2\"."));
        else if (!IsVersionRequirement(text))
            diagnostics.Add(new ConfigDiagnostic($"dependency '{name}' has an invalid version requirement '{text}'; expected something like \"^1.2\"."));
        else if (diagnostics.Count == reported)
            return new Dependency(name, text, isDevelopmentOnly);

        return null;
    }

    /// <summary>
    ///     Shape-checks a requirement: comma-separated clauses, each an optional comparator followed by a partial
    ///     version, or <c>*</c> for any version. Interpreting the comparators is left to the package manager.
    /// </summary>
    private static bool IsVersionRequirement(string requirement) =>
        requirement.Length > 0 && requirement.Split(',').All(clause => RequirementClause().IsMatch(clause));

    [GeneratedRegex(@"^\s*(?:\*|(?:\^|~|>=|<=|>|<|=)?\s*\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z.-]+)?)\s*$")]
    private static partial Regex RequirementClause();

    private static bool IsEdition(string edition) => edition.Length == EditionLength && edition.All(char.IsAsciiDigit);

    private static bool IsFetchableUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}