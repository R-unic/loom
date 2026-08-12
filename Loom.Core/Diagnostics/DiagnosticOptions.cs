namespace Loom.Core.Diagnostics;

/// <summary>
///     How the <see cref="DiagnosticBag" />s of one compilation behave when a diagnostic is reported. A bag
///     carries the options it was created with, so reporting behavior is a property of the compilation that
///     asked for it rather than of the process — two compilations in the same process cannot change how the
///     other reports.
/// </summary>
public sealed record DiagnosticOptions
{
    /// <summary>Used by bags created without options: collect everything, never end the process.</summary>
    public static readonly DiagnosticOptions Default = new();

    /// <summary>
    ///     Called with the first error reported to a bag, instead of letting the rest of the pipeline run.
    ///     Null by default, so a compilation collects everything.
    /// </summary>
    /// <remarks>
    ///     What "give up" means is the host's to decide, which is why this is a callback rather than the
    ///     print-and-exit it used to be. The CLI ends the process here because it has nothing left to do; a
    ///     language server, a test, or anything else embedding the compiler shares the pipeline that raised
    ///     the error and must not be killed by it. A diagnostic bag is a collection - it should not be able
    ///     to take down whoever is holding it.
    /// </remarks>
    public Action<Diagnostic>? OnFatalError { get; init; }

    /// <summary>Whether a compilation gives up at the first error rather than collecting them all.</summary>
    public bool FailFast => OnFatalError != null;

    /// <summary>
    ///     Report a dependency's diagnostics as they were raised, instead of as one error per file naming the
    ///     package that failed. Off by default: a warning in a package is not something the consumer of that
    ///     package can act on, and an error in one is a fact about the package rather than about their code.
    ///     Meant for debugging a package from a build that consumes it.
    /// </summary>
    public bool ReportDependencyDiagnostics { get; init; }
}
