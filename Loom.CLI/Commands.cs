using CommandLine;

namespace Loom.CLI;

internal abstract class ProjectCommand
{
    [Value(0, MetaName = "directory", Default = ".", HelpText = "The project directory.")]
    public string Directory { get; init; } = ".";
}

internal abstract class BuildCommand : ProjectCommand
{
    [Option('d', "dependency-diagnostics")]
    public bool DependencyDiagnostics { get; init; }
}

[Verb("build", HelpText = "Build a Loom project.")]
internal sealed class BuildOptions : BuildCommand;

[Verb("watch", HelpText = "Build a Loom project and watch for changes.")]
internal sealed class WatchOptions : BuildCommand;

[Verb("new", HelpText = "Create a new Loom project.")]
internal sealed class NewOptions : BuildCommand;