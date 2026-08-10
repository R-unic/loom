using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LoomDiagnostic = Loom.Core.Diagnostics.Diagnostic;
using Location = Loom.Core.Text.Location;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

/// <summary>
///     Turns the diagnostics that already say what to do into edits that do it. Every fix here corresponds to
///     a hint the compiler writes out in words - the code action is the same advice, applied.
/// </summary>
public sealed class CodeActionHandler(DocumentStore documents) : CodeActionHandlerBase
{
    public override Task<CommandOrCodeActionContainer?> Handle(CodeActionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<CommandOrCodeActionContainer?>(null);

        try
        {
            var text = state.File.SourceFile.SourceText;
            var range = TextSpan.FromStartEnd(IncrementalText.ToOffset(text, request.Range.Start), IncrementalText.ToOffset(text, request.Range.End));

            var actions = state.File.Diagnostics.Set
                .Where(diagnostic => Overlaps(diagnostic, range))
                .SelectMany(diagnostic => FixesFor(diagnostic, state, request.TextDocument.Uri))
                .Select(action => new CommandOrCodeAction(action))
                .ToArray();

            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer(actions));
        }
        catch (Exception)
        {
            return Task.FromResult<CommandOrCodeActionContainer?>(null);
        }
    }

    /// <summary>Every action arrives with its edit already built, so resolving one has nothing to add.</summary>
    public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(CodeActionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };

    private static IEnumerable<CodeAction> FixesFor(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri) =>
        diagnostic.Code switch
        {
            InternalCodes.CannotFindName => ImportFixes(diagnostic, state, uri),
            InternalCodes.UnusedImport => RemoveImportFix(diagnostic, state, uri),
            InternalCodes.PanicOutsideFallibleFunction => MarkFallibleFix(diagnostic, state, uri),
            _ => []
        };

    /// <summary>
    ///     One fix per module exporting the missing name. The name comes from the source the diagnostic
    ///     underlines rather than from its message: the message is prose meant for a person to read.
    /// </summary>
    private static IEnumerable<CodeAction> ImportFixes(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        var name = diagnostic.Span.GetText().ToString();
        if (name.Length == 0)
            return [];

        return ImportCatalog.For(state.File.SourceFile, state.Unit)
            .Where(candidate => candidate.Name == name)
            .GroupBy(candidate => candidate.Specifier)
            .Select(group => ImportEdits.Add(state.File, name, group.Key) is { } edit
                ? Fix($"Import '{name}' from \"{group.Key}\"", uri, edit, diagnostic)
                : null
            )
            .OfType<CodeAction>();
    }

    /// <summary>
    ///     Deletes an unused import - the whole statement when the name was the only one it brought in, and
    ///     just that name when it was not.
    /// </summary>
    private static IEnumerable<CodeAction> RemoveImportFix(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        var file = state.File.SourceFile;
        var node = NodeFinder.FindAt(state.File.Tree, diagnostic.Span.Start.Position);
        if (node is not ImportSpecifier specifier || specifier.Parent is not ImportDeclaration import)
            return node is ImportDeclaration whole ? [Fix("Remove unused import", uri, Delete(file, WholeLine(whole)), diagnostic)] : [];

        if (import.Specifiers.Count == 1)
            return [Fix("Remove unused import", uri, Delete(file, WholeLine(import)), diagnostic)];

        return [Fix($"Remove '{specifier.LocalName.Text}' from the import", uri, Delete(file, WithSeparator(import, specifier)), diagnostic)];
    }

    /// <summary>Marks the function the diagnostic blames, so the panic it already performs becomes one its signature admits to.</summary>
    private static IEnumerable<CodeAction> MarkFallibleFix(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        var node = NodeFinder.FindAt(state.File.Tree, diagnostic.Span.Start.Position);
        if (node?.FirstAncestorOfType<FunctionDeclaration>() is not { } function || HasFallible(function))
            return [];

        var indent = IndentOf(function);
        var edit = new TextEdit { Range = EmptyRangeAt(function.File, StartOfLine(function)), NewText = $"[fallible]\n{indent}" };
        return [Fix($"Mark '{function.Name.Text}' as '[fallible]'", uri, edit, diagnostic)];
    }

    private static bool HasFallible(FunctionDeclaration function) =>
        function.Attributes?.AttributeList.Exists(attribute => attribute.Expression.ToString().Contains("fallible")) ?? false;

    private static CodeAction Fix(string title, DocumentUri uri, TextEdit edit, LoomDiagnostic diagnostic) =>
        new()
        {
            Title = title,
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<LspDiagnostic>(Conversion.ToDiagnostic(diagnostic)),
            Edit = new WorkspaceEdit { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [uri] = [edit] } }
        };

    private static TextEdit Delete(SourceFile file, TextSpan span) =>
        new()
        {
            Range = new LspRange(
                Conversion.ToPosition(new Location(file, span.Position)),
                Conversion.ToPosition(new Location(file, span.End))
            ),
            NewText = ""
        };

    private static bool Overlaps(LoomDiagnostic diagnostic, TextSpan range) =>
        diagnostic.Span.Start.Position <= range.End && diagnostic.Span.End.Position >= range.Position;

    private static TextSpan WholeLine(Node node) => TextSpan.FromStartEnd(StartOfLine(node), EndOfLine(node));

    private static TextSpan WithSeparator(ImportDeclaration import, ImportSpecifier specifier)
    {
        var index = import.Specifiers.IndexOf(specifier);
        return index == 0
            ? TextSpan.FromStartEnd(specifier.Span.Position, import.Specifiers[1].Span.Position)
            : TextSpan.FromStartEnd(import.Specifiers[index - 1].Span.End, specifier.Span.End);
    }

    private static int StartOfLine(Node node)
    {
        var text = node.File.SourceText;
        var position = node.Span.Position;
        while (position > 0 && text[position - 1] != '\n')
            position--;

        return position;
    }

    private static int EndOfLine(Node node)
    {
        var text = node.File.SourceText;
        var position = node.Span.End;
        while (position < text.Length && text[position] != '\n')
            position++;

        return Math.Min(position + 1, text.Length);
    }

    private static string IndentOf(Node node) => new(' ', node.Span.Position - StartOfLine(node));

    private static LspRange EmptyRangeAt(SourceFile file, int position)
    {
        var at = Conversion.ToPosition(new Location(file, position));
        return new LspRange(at, at);
    }
}
