using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LoomDiagnostic = Loom.Core.Diagnostics.Diagnostic;
using LoomDiagnosticSeverity = Loom.Core.Diagnostics.DiagnosticSeverity;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using DiagnosticCode = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticCode;
using LspLocation = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using LoomLocation = Loom.Core.Text.Location;
using Position = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

public static class Conversion
{
    /// <summary>Codes whose warning means the code it marks does nothing, which editors gray out rather than underline.</summary>
    private static readonly HashSet<string> _unnecessaryCodes =
    [
        InternalCodes.UnreachableCode,
        InternalCodes.UnusedImport,
        InternalCodes.UnusedVariable,
        InternalCodes.UnusedParameter,
        InternalCodes.UnusedTypeParameter,
        InternalCodes.UnusedTrait
    ];

    public static Position ToPosition(LoomLocation location) => new(location.Line - 1, location.Character);

    public static Range ToRange(LocationSpan span) => new(ToPosition(span.Start), ToPosition(span.End));

    public static LspDiagnosticSeverity ToSeverity(LoomDiagnosticSeverity severity) =>
        severity switch
        {
            LoomDiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
            LoomDiagnosticSeverity.Warn => LspDiagnosticSeverity.Warning,
            _ => LspDiagnosticSeverity.Information
        };

    public static LspDiagnostic ToDiagnostic(LoomDiagnostic diagnostic) =>
        new()
        {
            Range = ToRange(diagnostic.Span),
            Severity = ToSeverity(diagnostic.Severity),
            Code = diagnostic.Code == null ? default : new DiagnosticCode(diagnostic.Code),
            Message = diagnostic.Message,
            // the hint says what to do about the message rather than restating it, so it is its own entry:
            // parenthesized onto the end it read as one long sentence, and the Problems panel showed it as one row
            RelatedInformation = ToRelatedInformation(diagnostic),
            Tags = ToTags(diagnostic),
            Source = "loom"
        };

    /// <summary>
    ///     What to publish for <paramref name="uri" /> after a compile. A null result means the document was
    ///     not analyzed at all - it is outside any project, or a stage threw - and the answer is an empty set
    ///     rather than nothing: a client keeps the last diagnostics it was sent, so publishing nothing leaves
    ///     the previous ones underlining text the user has since edited.
    /// </summary>
    public static LspDiagnostic[] DiagnosticsFor(CompilationResult? result, DocumentUri uri)
    {
        var rawPath = uri.GetFileSystemPath();
        if (result == null || string.IsNullOrEmpty(rawPath))
            return [];

        var path = Path.GetFullPath(rawPath);
        return [.. result.Diagnostics.InSourceOrder().Where(diagnostic => FilePaths.Same(diagnostic.Span.File.AbsolutePath, path)).Select(ToDiagnostic)];
    }

    /// <summary>
    ///     Everything the compile found, grouped by the file it was found in. A compile spans the whole
    ///     project, so an edit in one file routinely breaks another - reporting only the file that was edited
    ///     computes those and throws them away.
    /// </summary>
    /// <remarks>
    ///     Files with no path a client could open are left out: an intrinsic is an embedded resource, so there
    ///     is no document to attach its diagnostics to.
    /// </remarks>
    public static IReadOnlyDictionary<DocumentUri, LspDiagnostic[]> DiagnosticsByFile(CompilationResult result) =>
        result.Diagnostics.InSourceOrder()
            .Where(diagnostic => Path.IsPathRooted(diagnostic.Span.File.AbsolutePath))
            .GroupBy(diagnostic => diagnostic.Span.File.AbsolutePath, FilePaths.Comparer)
            .ToDictionary(group => DocumentUri.FromFileSystemPath(group.Key), group => group.Select(ToDiagnostic).ToArray());

    private static Container<DiagnosticRelatedInformation>? ToRelatedInformation(LoomDiagnostic diagnostic)
    {
        if (diagnostic.Hint is not { Length: > 0 } hint || ToLocation(diagnostic.Span) is not { } location)
            return null;

        return new Container<DiagnosticRelatedInformation>(new DiagnosticRelatedInformation { Location = location, Message = hint });
    }

    private static Container<DiagnosticTag>? ToTags(LoomDiagnostic diagnostic)
    {
        if (diagnostic.Code == InternalCodes.DeprecatedMember)
            return new Container<DiagnosticTag>(DiagnosticTag.Deprecated);

        return diagnostic.Code != null && _unnecessaryCodes.Contains(diagnostic.Code)
            ? new Container<DiagnosticTag>(DiagnosticTag.Unnecessary)
            : null;
    }

    /// <summary>
    ///     Where a span is, as a location a client can navigate to, or null for a span the client cannot open -
    ///     an intrinsic is an embedded resource with no path, and a synthesized node may have no file at all.
    /// </summary>
    public static LspLocation? ToLocation(LocationSpan span)
    {
        var path = span.File.AbsolutePath;
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
            return null;

        return new LspLocation
        {
            Uri = DocumentUri.FromFileSystemPath(path), Range = ToRange(span)
        };
    }
}
