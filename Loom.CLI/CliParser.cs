using System.Reflection;

namespace Loom.CLI;

/// <summary>
///     Hand-rolled replacement for CommandLineParser: under Native AOT it discovers <c>[Verb]</c> option types
///     by reflection and crashes constructing them, rather than degrading gracefully. The surface here is small
///     enough (three verbs, one positional argument, one flag) that reflection buys nothing.
/// </summary>
internal static class CliParser
{
    private const string ProductName = "loom";

    private static readonly (string Label, string Description)[] TopLevelVerbs =
    [
        ("build", "Build a Loom project."),
        ("watch", "Build a Loom project and watch for changes."),
        ("new", "Create a new Loom project."),
        ("help", "Display more information on a specific command."),
        ("version", "Display version information.")
    ];

    private static readonly (string Label, string Description)[] GlobalOptions =
    [
        ("--help", "Display this help screen."),
        ("--version", "Display version information.")
    ];

    private static readonly (string Label, string Description)[] BuildWatchOptions =
    [
        ("-d, --dependency-diagnostics", ""),
        ("--help", "Display this help screen."),
        ("--version", "Display version information."),
        ("directory (pos. 0)", "(Default: .) The project directory.")
    ];

    private static readonly (string Label, string Description)[] NewCommandOptions =
    [
        ("--help", "Display this help screen."),
        ("--version", "Display version information."),
        ("directory (pos. 0)", "(Default: .) The project directory.")
    ];

    private static string Version { get; } =
        typeof(CliParser).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return Error("No verb selected.", TopLevelVerbs);

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
                return ParseBuildLike(rest, BuildWatchOptions, (directory, dependencyDiagnostics) => new CliCommand.RunBuild(new BuildOptions(directory, dependencyDiagnostics)));

            case "watch":
                return ParseBuildLike(rest, BuildWatchOptions, (directory, dependencyDiagnostics) => new CliCommand.RunWatch(new WatchOptions(directory, dependencyDiagnostics)));

            case "new":
                return ParseNew(rest);

            case var verb:
                return Error($"Verb '{verb}' is not recognized.", GlobalOptions);
        }
    }

    private static bool TryGetVerbOptions(string verb, out (string Label, string Description)[] options)
    {
        switch (verb)
        {
            case "build" or "watch":
                options = BuildWatchOptions;
                return true;
            case "new":
                options = NewCommandOptions;
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

                case var flag when flag.StartsWith('-'):
                    return Error($"Option '{flag.TrimStart('-')}' is unknown.", options);

                case var positional:
                    if (!directorySet)
                    {
                        directory = positional;
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
                    PrintVerbHelp(NewCommandOptions);
                    return new CliCommand.Done(0);

                case "--version":
                    PrintVersion();
                    return new CliCommand.Done(0);

                case var flag when flag.StartsWith('-'):
                    return Error($"Option '{flag.TrimStart('-')}' is unknown.", NewCommandOptions);

                case var positional:
                    if (!directorySet)
                    {
                        directory = positional;
                        directorySet = true;
                    }

                    break;
            }
        }

        return new CliCommand.RunNew(new NewOptions(directory));
    }

    private static CliCommand Error(string message, (string Label, string Description)[] fallbackOptions)
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
        PrintEntries(TopLevelVerbs);
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
