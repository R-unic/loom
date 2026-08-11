using Loom.Core.Diagnostics;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

public sealed record CompilationResult(List<CompiledFile> Files, DiagnosticBag Diagnostics)
    : DiagnosedResult(Diagnostics)
{
    /// <summary>
    ///     Files the compiler gave up on. Their diagnostics are part of <see cref="Diagnostics" /> as well;
    ///     this names which files are missing from <see cref="Files" /> and why.
    /// </summary>
    public List<FailedFile> Failures { get; init; } = [];

    /// <summary>Files actually re-parsed and re-analyzed this call, as opposed to reused from a prior compile's cache.</summary>
    public IReadOnlyList<SourceFile> Reanalyzed { get; init; } = [];

    /// <summary>
    ///     Whether the compile failed: a file the compiler gave up on, or an error reported against the unit or
    ///     any one of its files. This is how a caller compiling on someone else's behalf decides success - the
    ///     CLI's exit code, or a package manager building a dependency - rather than by whether
    ///     <see cref="DiagnosticOptions.FailFast" /> happened to kill the process, which only holds for a caller
    ///     that turned it on and only for the files it turned it on for.
    /// </summary>
    public bool Failed =>
        Failures.Count > 0
        || Diagnostics.ContainsErrors()
        || Files.Any(file => file.Diagnostics.ContainsErrors());

    /// <summary>Wall-clock time the call took, start to finish.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    ///     Sum of the last-measured analysis time of every file this call skipped reusing from cache. Zero for
    ///     a full <see cref="CompilationUnit.Compile()" />, since nothing is skipped there.
    /// </summary>
    public TimeSpan EstimatedTimeSaved { get; init; }
}