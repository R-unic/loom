namespace Loom.CLI;

internal readonly record struct BuildOptions(string Directory, bool DependencyDiagnostics);

internal readonly record struct WatchOptions(string Directory, bool DependencyDiagnostics);

internal readonly record struct NewOptions(string Directory);

internal abstract record CliCommand
{
    private CliCommand() { }

    public sealed record RunBuild(BuildOptions Options) : CliCommand;

    public sealed record RunWatch(WatchOptions Options) : CliCommand;

    public sealed record RunNew(NewOptions Options) : CliCommand;

    public sealed record Done(int ExitCode) : CliCommand;
}
