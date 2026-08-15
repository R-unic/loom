using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Diagnostics;

public sealed class DiagnosticBag(HashSet<Diagnostic>? diagnostics = null, DiagnosticOptions? options = null)
{
    public HashSet<Diagnostic> Set { get; } = diagnostics ?? [];

    /// <summary>Reporting behavior of this bag; <see cref="DiagnosticOptions.Default" /> when unspecified.</summary>
    public DiagnosticOptions Options { get; } = options ?? DiagnosticOptions.Default;

    /// <summary>
    ///     Merges the diagnostics of <paramref name="bags" /> into one bag. The merged bag reports with
    ///     <paramref name="options" />, defaulting to the options of the first bag, since every bag of one
    ///     compilation shares them.
    /// </summary>
    public static DiagnosticBag Concat(List<DiagnosticBag> bags, DiagnosticOptions? options = null) =>
        new(bags.SelectMany(bag => bag.Set).ToHashSet(), options ?? bags.FirstOrDefault()?.Options);

    public void Info(Node node, string message) => Info(node.LocationSpan, message);
    public void Info(LocationSpan span, string message) => Report(span, DiagnosticSeverity.Info, null, message, null);
    public void Info(Node node, string code, string message) => Info(node.LocationSpan, code, message);
    public void Info(LocationSpan span, string code, string message) => Report(span, DiagnosticSeverity.Info, code, message, null);
    public void Warn(Node node, string code, string message, string? hint = null) => Warn(node.LocationSpan, code, message, hint);
    public void Warn(Token token, string code, string message, string? hint = null) => Warn(token.GetLocation(), code, message, hint);
    public void Warn(LocationSpan span, string code, string message, string? hint = null) => Report(span, DiagnosticSeverity.Warn, code, message, hint);
    public void Error(Node node, string code, string message, string? hint = null) => Error(node.LocationSpan, code, message, hint);
    public void Error(Token token, string code, string message, string? hint = null) => Error(token.GetLocation(), code, message, hint);
    public void Error(LocationSpan span, string code, string message, string? hint = null) => Report(span, DiagnosticSeverity.Error, code, message, hint);
    public void NotImplemented(Node node, string? feature = null, string? hint = null) => NotImplemented(node.LocationSpan, feature, hint);
    public void NotImplemented(Token token, string? feature = null, string? hint = null) => NotImplemented(token.GetLocation(), feature, hint);

    private void NotImplemented(LocationSpan span, string? feature = null, string? hint = null) =>
        Error(span, InternalCodes.NotImplemented, feature ?? "This feature is not yet implemented.", hint);

    public void CompilerError(Node node, string message) => CompilerError(node.LocationSpan, message);
    public void CompilerError(SourceFile file, string message) => CompilerError(LocationSpan.Empty(file), message);

    private void CompilerError(LocationSpan span, string message) =>
        Error(span, InternalCodes.CompilerError, message, "this is a compiler bug! please report an issue.");

    /// <summary>
    ///     This bag as the consumer of <paramref name="package" /> should see it: nothing at all when the
    ///     package compiled, and a single error naming the package when it did not, carrying the first
    ///     underlying error and, at its location, the code it was raised against.
    /// </summary>
    /// <remarks>
    ///     A package's warnings are dropped rather than shown, because the reader of a consumer's build cannot
    ///     act on them — the code is not theirs to change. Its errors cannot be dropped, since nothing can be
    ///     built on a package that did not compile, but they are framed so that the reader knows whose problem
    ///     they are looking at. Reporting one error per file rather than all of them keeps a package that fails
    ///     in a big way from burying the reader's own diagnostics.
    /// </remarks>
    /// <param name="package"></param>
    /// <param name="reportingOptions">
    ///     Options the attributed error is reported with, the file's own when unspecified. The caller hands its
    ///     own here so that a build set to fail fast still stops on a package that failed, even though the
    ///     package's files are compiled without failing fast — the point of that being to reach this error
    ///     rather than to exit on the raw one.
    /// </param>
    public DiagnosticBag AttributedTo(string package, DiagnosticOptions? reportingOptions = null)
    {
        var errors = InSourceOrder().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        var options = reportingOptions ?? Options;
        if (errors.Count == 0)
            return new DiagnosticBag(options: options);

        var first = errors[0];
        var others = errors.Count == 1 ? "" : $" ({errors.Count - 1} more error{(errors.Count == 2 ? "" : "s")} in this file)";
        var attributed = new DiagnosticBag(options: options);
        attributed.Report(
            first.Span,
            DiagnosticSeverity.Error,
            InternalCodes.PackageFailedToCompile,
            $"Package '{package}' failed to compile: {first.Message}{others}",
            first.Hint ?? "this is a problem in the package rather than in your code"
        );

        return attributed;
    }

    /// <summary>
    ///     Everything in the bag, ordered by where it was raised: by file, then by position within it, then
    ///     by what was said there.
    /// </summary>
    /// <remarks>
    ///     <see cref="Set" /> is a set because a diagnostic must not be reported twice, and the order a set
    ///     enumerates in is its own business - it follows the order the pipeline happened to visit things,
    ///     which is neither the order a reader scans a file in nor stable across runs. Every compiler that
    ///     prints diagnostics sorts them by position for the same two reasons, so reading a build's output
    ///     top to bottom is reading the file top to bottom, and so two builds of one tree print the same
    ///     bytes.
    /// </remarks>
    public IReadOnlyList<Diagnostic> InSourceOrder() =>
        Set.OrderBy(diagnostic => diagnostic.Span.File.AbsolutePath, PathComparison.Comparer)
            .ThenBy(diagnostic => diagnostic.Span.Start.Position)
            .ThenBy(diagnostic => diagnostic.Span.End.Position)
            .ThenBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToList();

    public Diagnostic? Find(Func<Diagnostic, bool> predicate) => Set.FirstOrDefault(predicate);
    public DiagnosticBag WithoutInfo() => new(Set.Where(d => d.Severity > DiagnosticSeverity.Info).ToHashSet(), Options);
    public DiagnosticBag Errors() => new(Set.Where(d => d.Severity == DiagnosticSeverity.Error).ToHashSet(), Options);
    public bool ContainsErrors() => Set.Any(d => d.Severity == DiagnosticSeverity.Error);

    public override string ToString() => string.Join('\n', InSourceOrder());

    private void Report(LocationSpan span, DiagnosticSeverity severity, string? code, string message, string? hint) =>
        Report(new Diagnostic(span, severity, code, message, hint));

    private void Report(Diagnostic diagnostic)
    {
        Set.Add(diagnostic);
        if (diagnostic.Severity >= DiagnosticSeverity.Error)
            Options.OnFatalError?.Invoke(diagnostic);
    }
}