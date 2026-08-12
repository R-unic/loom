using System.Diagnostics.CodeAnalysis;
using System.Text;
using CommandLine;
using Loom.CLI;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

var cliParser = new Parser(settings =>
    {
        settings.CaseInsensitiveEnumValues = true;
        settings.HelpWriter = Console.Out;
        settings.AutoHelp = true;
        settings.AutoVersion = true;
    }
);

return cliParser
    .ParseArguments<BuildOptions, WatchOptions, NewOptions>(args)
    .MapResult(
        (BuildOptions options) => compile(options, watch: false),
        (WatchOptions options) => compile(options, watch: true),
        (NewOptions options) => Scaffolder.NewProject(options.Directory),
        handleParseError
    );

static bool tryGetConfig(string directory, [NotNullWhen(true)] out LoomConfig? config)
{
    config = ConfigReader.LocateFromDirectory(directory, out var configDiagnostics);
    if (config != null)
        return true;

    if (configDiagnostics.Count == 0)
        Log.Fatal($"could not locate Loom configuration file in directory '{directory}'.");

    Console.WriteLine(string.Join(Environment.NewLine, configDiagnostics.Select(diagnostic => $"({ConfigReader.ConfigFileName}) {diagnostic}")));
    return false;
}

static int compile(BuildCommand options, bool watch)
{
    Console.OutputEncoding = Encoding.UTF8;

    // a one-shot build has nothing left to do once a file fails, so it stops at the first error; a watch
    // has to stay up and report the next save, so it collects them like any other embedder does
    var diagnosticOptions = new DiagnosticOptions
    {
        OnFatalError = watch ? null : printAndExit, ReportDependencyDiagnostics = options.DependencyDiagnostics
    };

    if (!tryGetConfig(options.Directory, out var config))
        return 1;

    FileManager.WriteIncludeFolder(config.ProjectDirectory);
    if (!watch)
    {
        var result = new CompilationUnit(config, diagnosticOptions).Compile();
        Log.OutputResult(result);
        return result.Failed ? 1 : 0;
    }

    var watcher = new Watcher(diagnosticOptions);
    return watcher.Start(config);
}

static void printAndExit(Diagnostic diagnostic)
{
    Console.WriteLine(diagnostic.ToString());
    Environment.Exit(1);
}

static int handleParseError(IEnumerable<Error> errors)
{
    // Asking for help or for the version is not a failure: the parser has already written the
    // answer to the help writer, and exiting non-zero would fail any script that asked.
    var parseErrors = errors as IReadOnlyList<Error> ?? errors.ToList();
    if (parseErrors.IsHelp() || parseErrors.IsVersion())
        return 0;

    Log.Fatal("invalid command");
    return 1;
}