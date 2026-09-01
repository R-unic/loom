using System.Reflection;

namespace Loom.CLI;

/// <summary>
///     Hand-rolled replacement for CommandLineParser: under Native AOT it discovers <c>[Verb]</c> option types
///     by reflection and crashes constructing them, rather than degrading gracefully. The surface here is small
///     enough — a handful of verbs, a positional argument each and a flag or two — that reflection buys nothing.
/// </summary>
internal static class CliParser
{
    private const string ProductName = "loom";

    private static readonly (string Label, string Description)[] _topLevelVerbs =
    [
        ("build", "Build a Loom project."),
        ("watch", "Build a Loom project and watch for changes."),
        ("new", "Create a new Loom project."),
        ("add", "Add a dependency to a Loom project."),
        ("publish", "Publish a Loom package to its index."),
        ("login", "Sign in to a package registry."),
        ("help", "Display more information on a specific command."),
        ("version", "Display version information.")
    ];

    private static readonly (string Label, string Description)[] _globalOptions =
    [
        ("--help", "Display this help screen."),
        ("--version", "Display version information.")
    ];
    
    private static readonly (string Label, string Description)[] _projectOptions =
    [
        .._globalOptions,
        ("directory (pos. 0)", "(Default: .) The project directory.")
    ];

    private static readonly (string Label, string Description)[] _buildWatchOptions =
    [
        ("-d, --dependency-diagnostics", "Report a dependency's own diagnostics instead of collapsing them into one error per file."),
        .._projectOptions
    ];

    private static readonly (string Label, string Description)[] _newCommandOptions = _projectOptions;

    private static readonly (string Label, string Description)[] _addCommandOptions =
    [
        ("-D, --dev", "Add the packages as development-only dependencies."),
        ("-p, --project", "(Default: .) The project directory."),
        .._globalOptions,
        ("packages (pos. 0..)", "The packages to add, each written 'name' or 'name@requirement'.")
    ];

    private static readonly (string Label, string Description)[] _publishCommandOptions =
    [
        ("-n, --dry-run", "List what would be published, without publishing it."),
        ("--allow-dirty", "Publish without checking that the project compiles."),
        .._projectOptions
    ];

    private static readonly (string Label, string Description)[] _loginCommandOptions =
    [
        ("-p, --project", "(Default: .) The project whose registry to sign in to."),
        ("-t, --token", "The token to store; '-' reads it from standard input."),
        ("--help", "Display this help screen."),
        ("--version", "Display version information."),
        ("registry (pos. 0)", "The registry to sign in to, when it is not the project's own.")
    ];

    private static string Version { get; } =
        typeof(CliParser).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return Error("No verb selected.", _topLevelVerbs);

        var rest = args[1..];
        switch (args[0])
        {
            case "--help":
                PrintTopHelp();
                return new CliCommand.Done(0);

            case "--version" or "version":
                PrintVersion();
                return new CliCommand.Done(0);

            case "help":
                if (rest.Length > 0 && TryGetVerbOptions(rest[0], out var options))
                    PrintVerbHelp(options);
                else
                    PrintTopHelp();
                return new CliCommand.Done(0);

            case "build":
                return ParseBuildLike(rest, _buildWatchOptions, (directory, dependencyDiagnostics) => new CliCommand.RunBuild(new BuildOptions(directory, dependencyDiagnostics)));

            case "watch":
                return ParseBuildLike(rest, _buildWatchOptions, (directory, dependencyDiagnostics) => new CliCommand.RunWatch(new WatchOptions(directory, dependencyDiagnostics)));

            case "new":
                return ParseNew(rest);

            case "add":
                return ParseAdd(rest);

            case "publish":
                return ParsePublish(rest);

            case "login":
                return ParseLogin(rest);

            case var verb:
                return Error($"Verb '{verb}' is not recognized.", _topLevelVerbs);
        }
    }

    private static bool TryGetVerbOptions(string verb, out (string Label, string Description)[] options)
    {
        switch (verb)
        {
            case "build" or "watch":
                options = _buildWatchOptions;
                return true;
            case "new":
                options = _newCommandOptions;
                return true;
            case "add":
                options = _addCommandOptions;
                return true;
            case "publish":
                options = _publishCommandOptions;
                return true;
            case "login":
                options = _loginCommandOptions;
                return true;
            default:
                options = [];
                return false;
        }
    }

    private static CliCommand ParseBuildLike(
        string[] rest,
        (string Label, string Description)[] options,
        Func<string, bool, CliCommand> create)
    {
        var directory = ".";
        var directorySet = false;
        var dependencyDiagnostics = false;

        foreach (var arg in rest)
        {
            switch (arg)
            {
                case "--help":
                    PrintVerbHelp(options);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case "-d" or "--dependency-diagnostics":
                    dependencyDiagnostics = true;
                    break;

                case var _ when arg.StartsWith('-'):
                    return Error($"Option '{arg.TrimStart('-')}' is unknown.", options);

                case var _:
                    if (!directorySet)
                    {
                        directory = arg;
                        directorySet = true;
                    }

                    break;
            }
        }

        return create(directory, dependencyDiagnostics);
    }

    private static CliCommand ParseNew(string[] rest)
    {
        var directory = ".";
        var directorySet = false;

        foreach (var arg in rest)
        {
            switch (arg)
            {
                case "--help":
                    PrintVerbHelp(_newCommandOptions);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case var _ when arg.StartsWith('-'):
                    return Error($"Option '{arg.TrimStart('-')}' is unknown.", _newCommandOptions);

                case var _:
                    if (!directorySet)
                    {
                        directory = arg;
                        directorySet = true;
                    }

                    break;
            }
        }

        return new CliCommand.RunNew(new NewOptions(directory));
    }

    /// <summary>
    ///     Reads <c>add</c>: every positional is a package to add, so the project directory is named by an option
    ///     rather than by position as the other verbs allow.
    /// </summary>
    private static CliCommand ParseAdd(string[] rest)
    {
        var packages = new List<string>();
        var directory = ".";
        var developmentOnly = false;

        for (var index = 0; index < rest.Length; index++)
        {
            switch (rest[index])
            {
                case "--help":
                    PrintVerbHelp(_addCommandOptions);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case "-D" or "--dev":
                    developmentOnly = true;
                    break;

                case "-p" or "--project":
                    if (index + 1 >= rest.Length)
                        return Error("Option 'project' has no value.", _addCommandOptions);

                    directory = rest[++index];
                    break;

                case var flag when flag.StartsWith('-'):
                    return Error($"Option '{flag.TrimStart('-')}' is unknown.", _addCommandOptions);

                case var positional:
                    packages.Add(positional);
                    break;
            }
        }

        if (packages.Count == 0)
            return Error("No package to add was named.", _addCommandOptions);

        return new CliCommand.RunAdd(new AddOptions(packages, developmentOnly, directory));
    }

    private static CliCommand ParsePublish(string[] rest)
    {
        var directory = ".";
        var directorySet = false;
        var dryRun = false;
        var allowDirty = false;

        foreach (var arg in rest)
        {
            switch (arg)
            {
                case "--help":
                    PrintVerbHelp(_publishCommandOptions);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case "-n" or "--dry-run":
                    dryRun = true;
                    break;

                case "--allow-dirty":
                    allowDirty = true;
                    break;

                case var flag when flag.StartsWith('-'):
                    return Error($"Option '{flag.TrimStart('-')}' is unknown.", _publishCommandOptions);

                case var positional:
                    if (!directorySet)
                    {
                        directory = positional;
                        directorySet = true;
                    }

                    break;
            }
        }

        return new CliCommand.RunPublish(new PublishOptions(directory, dryRun, allowDirty));
    }

    /// <summary>
    ///     Reads <c>login</c>: the positional is a registry rather than a project, since signing in is about a
    ///     registry and the project is only how one is found when none is named.
    /// </summary>
    private static CliCommand ParseLogin(string[] rest)
    {
        string? registry = null;
        var directory = ".";
        string? token = null;

        for (var index = 0; index < rest.Length; index++)
        {
            switch (rest[index])
            {
                case "--help":
                    PrintVerbHelp(_loginCommandOptions);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case "-p" or "--project":
                    if (index + 1 >= rest.Length)
                        return Error("Option 'project' has no value.", _loginCommandOptions);

                    directory = rest[++index];
                    break;

                case "-t" or "--token":
                    if (index + 1 >= rest.Length)
                        return Error("Option 'token' has no value.", _loginCommandOptions);

                    token = rest[++index];
                    break;

                case var flag when flag.StartsWith('-') && flag.Length > 1:
                    return Error($"Option '{flag.TrimStart('-')}' is unknown.", _loginCommandOptions);

                case var positional:
                    registry ??= positional;
                    break;
            }
        }

        return new CliCommand.RunLogin(new LoginOptions(registry, directory, token));
    }

    private static CliCommand.Done Error(string message, (string Label, string Description)[] fallbackOptions)
    {
        PrintHeader();
        Console.WriteLine("ERROR(S):");
        Console.WriteLine($"  {message}");
        Console.WriteLine();
        PrintEntries(fallbackOptions);
        return new CliCommand.Done(1);
    }

    private static void PrintTopHelp()
    {
        PrintHeader();
        PrintEntries(_topLevelVerbs);
    }

    private static void PrintVerbHelp((string Label, string Description)[] options)
    {
        PrintHeader();
        PrintEntries(options);
    }

    private static void PrintVersion() => Console.WriteLine($"{ProductName} {Version}");

    private static void PrintHeader()
    {
        Console.WriteLine($"{ProductName} {Version}");
        Console.WriteLine($"Copyright (C) {DateTime.Now.Year} {ProductName}");
        Console.WriteLine();
    }

    private static void PrintEntries((string Label, string Description)[] entries)
    {
        var width = entries.Max(entry => entry.Label.Length) + 4;
        foreach (var (label, description) in entries)
        {
            Console.WriteLine($"  {label.PadRight(width)}{description}".TrimEnd());
            Console.WriteLine();
        }
    }
}
