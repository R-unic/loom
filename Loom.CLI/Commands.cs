namespace Loom.CLI;

internal readonly record struct BuildOptions(string Directory, bool DependencyDiagnostics);

internal readonly record struct WatchOptions(string Directory, bool DependencyDiagnostics);

internal readonly record struct NewOptions(string Directory);

/// <param name="Packages">
///     The packages to add, each written <c>name</c> or <c>name@requirement</c>. Read into a
///     <see cref="Loom.Packages.PackageRequest" /> once the project is known, so that an unreadable one is reported
///     with the project's other problems rather than as a parse error.
/// </param>
internal readonly record struct AddOptions(IReadOnlyList<string> Packages, bool DevelopmentOnly, string Directory);

/// <param name="AllowDirty">
///     Publish without first checking that the project compiles. Named for what it lets through rather than for the
///     check it skips: what reaches the index is source nobody has established is publishable.
/// </param>
internal readonly record struct PublishOptions(string Directory, bool DryRun, bool AllowDirty);

internal abstract record CliCommand
{
    private CliCommand() { }

    public sealed record RunBuild(BuildOptions Options) : CliCommand;

    public sealed record RunWatch(WatchOptions Options) : CliCommand;

    public sealed record RunNew(NewOptions Options) : CliCommand;

    public sealed record RunAdd(AddOptions Options) : CliCommand;

    public sealed record RunPublish(PublishOptions Options) : CliCommand;

    public sealed record Done(int ExitCode) : CliCommand;
}
