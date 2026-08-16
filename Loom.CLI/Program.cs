using System.Diagnostics.CodeAnalysis;
using System.Text;
using Loom.CLI;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

var command = CliParser.Parse(args);
return command switch
{
    CliCommand.RunBuild build => compile(build.Options.Directory, build.Options.DependencyDiagnostics, watch: false),
    CliCommand.RunWatch watchCommand => compile(watchCommand.Options.Directory, watchCommand.Options.DependencyDiagnostics, watch: true),
    CliCommand.RunNew newCommand => Scaffolder.NewProject(newCommand.Options.Directory),
    CliCommand.Done done => reportDone(done.ExitCode),
    _ => 1
};

static int reportDone(int exitCode)
{
    if (exitCode != 0)
        Log.Fatal("invalid command");

    return exitCode;
}

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

static int compile(string directory, bool dependencyDiagnostics, bool watch)
{
    Console.OutputEncoding = Encoding.UTF8;

    // a one-shot build has nothing left to do once a file fails, so it stops at the first error; a watch
    // has to stay up and report the next save, so it collects them like any other embedder does
    var diagnosticOptions = new DiagnosticOptions
    {
        OnFatalError = watch ? null : printAndExit, ReportDependencyDiagnostics = dependencyDiagnostics
    };

    if (!tryGetConfig(directory, out var config))
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
