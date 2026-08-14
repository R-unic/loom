using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LoomDiagnostic = Loom.Core.Diagnostics.Diagnostic;
using LoomAttribute = Loom.Core.Parsing.AST.Attribute;
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

            var uri = request.TextDocument.Uri;

            // computed once per diagnostic and shared: the range-filtered list and FixAll both ask "what
            // fixes this", and a diagnostic in range is exactly the case both would otherwise redo
            var fixesByDiagnostic = state.File.Diagnostics.Set.ToDictionary(diagnostic => diagnostic, diagnostic => FixesFor(diagnostic, state, uri).ToArray());

            var actions = fixesByDiagnostic
                .Where(entry => Overlaps(entry.Key, range))
                .SelectMany(entry => entry.Value)
                .Concat(OrganizeImports(state, uri))
                .Concat(FixAll(state, uri, fixesByDiagnostic))
                .Where(action => IsWanted(action, request.Context.Only))
                .Select(action => new CommandOrCodeAction(action))
                .ToArray();

            return Task.FromResult<CommandOrCodeActionContainer?>(new CommandOrCodeActionContainer(actions));
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
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
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix, CodeActionKind.SourceOrganizeImports, CodeActionKind.SourceFixAll),
            ResolveProvider = false
        };

    /// <summary>
    ///     Whether the client asked for this kind. A request naming kinds wants only those - the editor asks
    ///     for <c>source.organizeImports</c> alone when the user runs the command, and for nothing in
    ///     particular when the lightbulb opens. A named kind matches its sub-kinds too, so asking for
    ///     <c>source</c> is asking for every source action.
    /// </summary>
    private static bool IsWanted(CodeAction action, Container<CodeActionKind>? only)
    {
        if (only is not { } kinds || !kinds.Any())
            return true;

        var kind = action.Kind.ToString();
        return kinds.Any(wanted => kind == wanted.ToString() || kind.StartsWith(wanted + ".", StringComparison.Ordinal));
    }

    private static IEnumerable<CodeAction> FixesFor(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri) =>
        diagnostic.Code switch
        {
            InternalCodes.CannotFindName => ImportFixes(diagnostic, state, uri),
            InternalCodes.UnusedImport => RemoveImportFix(diagnostic, state, uri),
            InternalCodes.PanicOutsideFallibleFunction => MarkFallibleFix(diagnostic, state, uri),
            InternalCodes.RedundantCode => RedundantCodeFixes(diagnostic, state, uri),
            InternalCodes.UnreachableCode => RemoveUnreachableFix(diagnostic, state, uri),
            InternalCodes.TypeOnlyExportOfValue => RemoveTypeKeywordFix(diagnostic, state, uri, "Remove 'type' from the export"),
            InternalCodes.TypeOnlyImportOfValue => RemoveTypeKeywordFix(diagnostic, state, uri, "Remove 'type' from the import"),
            InternalCodes.CannotExportMutable => UseLetInsteadOfMutFix(diagnostic, state, uri),
            InternalCodes.YieldInNoYieldContext => YieldInNoYieldContextFixes(diagnostic, state, uri),
            _ => []
        };

    /// <summary>
    ///     Deletes the <c>type</c> keyword an import or export list was written with. Deleted from where the
    ///     preceding keyword ends rather than from the token's own start, so the whitespace either side of
    ///     <c>type</c> collapses to one space instead of leaving a gap or crowding two words together.
    /// </summary>
    private static IEnumerable<CodeAction> RemoveTypeKeywordFix(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri, string title)
    {
        var file = state.File.SourceFile;
        return NodeOf(diagnostic, state.File)?.Parent switch
        {
            ExportList { TypeKeyword: { } keyword } list =>
                [Fix(title, uri, Delete(file, TextSpan.FromStartEnd(list.ExportKeyword.Span.End, keyword.Span.End)), diagnostic)],
            ImportDeclaration { TypeKeyword: { } keyword } import =>
                [Fix(title, uri, Delete(file, TextSpan.FromStartEnd(import.ImportKeyword.Span.End, keyword.Span.End)), diagnostic)],
            _ => []
        };
    }

    private static IEnumerable<CodeAction> UseLetInsteadOfMutFix(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri) =>
        NodeOf(diagnostic, state.File) is ExportDeclaration { Declaration: VariableDeclaration { Keyword.Kind: SyntaxKind.MutKeyword } variable }
            ? [Fix("Use 'let' instead of 'mut'", uri, Replace(state.File.SourceFile, variable.Keyword.Span, "let"), diagnostic)]
            : [];

    /// <summary>
    ///     'async' and '[no_yield]' on the same declaration disagree about whether it yields, and nothing here
    ///     knows which one the author meant - so both ways out are offered rather than one chosen for them.
    /// </summary>
    private static IEnumerable<CodeAction> YieldInNoYieldContextFixes(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        if (NodeOf(diagnostic, state.File) is not FunctionDeclaration { AsyncKeyword: { } asyncKeyword } function)
            return [];

        var file = state.File.SourceFile;
        var fixes = new List<CodeAction>
        {
            Fix("Drop 'async'", uri, Delete(file, TextSpan.FromStartEnd(asyncKeyword.Span.Position, function.Keyword.Span.Position)), diagnostic)
        };

        if (function.Attributes?.AttributeList.Find(attribute => attribute.Name == "no_yield") is { } noYield)
            fixes.Add(Fix("Drop '[no_yield]'", uri, RemoveAttributeEdit(file, function.Attributes, noYield), diagnostic));

        return fixes;
    }

    /// <summary>
    ///     One code covers four different redundancies, so which fix applies is decided by the syntax the
    ///     diagnostic underlines rather than by its message - the message is prose meant for a person, and a
    ///     fix keyed on it would break the first time one is reworded.
    /// </summary>
    private static IEnumerable<CodeAction> RedundantCodeFixes(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        var file = state.File.SourceFile;
        switch (NodeOf(diagnostic, state.File))
        {
            // a body that only returns says in three lines what '->' says in one, which is what the compiler
            // has already told the reader to do
            case IFunctionLike { Body: Block { Statements: [Return { Expression: { } returned }] } body }:
                return [Fix("Use an expression body", uri, Replace(file, body.Span, $"-> {TextOf(file, returned.Span)};"), diagnostic)];
            case NullForgiving forgiving:
                return [Fix("Remove the redundant '!'", uri, Delete(file, forgiving.Bang.Span), diagnostic)];
            // only '??', never '??=': the compound form is the whole assignment, and deleting its right-hand
            // side would leave a statement that assigns nothing
            case BinaryOperator { Operator.Kind: SyntaxKind.QuestionQuestion } coalesce:
                return
                [
                    Fix(
                        "Remove the redundant '??'",
                        uri,
                        Delete(file, TextSpan.FromStartEnd(coalesce.Left.Span.End, coalesce.Right.Span.End)),
                        diagnostic
                    )
                ];
            default:
                return [];
        }
    }

    private static IEnumerable<CodeAction> RemoveUnreachableFix(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri) =>
        NodeOf(diagnostic, state.File) is { } node
            ? [Fix("Remove unreachable code", uri, Delete(state.File.SourceFile, WholeLine(node)), diagnostic)]
            : [];

    /// <summary>
    ///     Removes every import the file does not use, in one edit. The editor's Organize Imports command asks
    ///     for this by kind, so it is offered whatever the cursor is on rather than only where a warning is.
    /// </summary>
    /// <remarks>
    ///     Nothing is reordered. Sorting an import list is a change to code the user wrote, and until there is
    ///     a formatter to say what order is the right one, the server would be inventing one of its own.
    /// </remarks>
    private static IEnumerable<CodeAction> OrganizeImports(DocumentState state, DocumentUri uri)
    {
        var file = state.File.SourceFile;
        var unused = state.File.Diagnostics.Set.Where(diagnostic => diagnostic.Code == InternalCodes.UnusedImport).ToArray();
        if (unused.Length == 0)
            return [];

        var specifiers = unused.Select(diagnostic => NodeOf(diagnostic, state.File)).OfType<Node>().ToArray();
        var edits = new List<TextEdit>();
        foreach (var group in specifiers.GroupBy(node => node is ImportSpecifier ? node.Parent : node))
        {
            // an import whose every name is unused goes entirely, rather than being hollowed out into an
            // 'import { } from "…"' that still runs
            if (group.Key is ImportDeclaration import && import.Specifiers.TrueForAll(specifier => group.Contains(specifier)))
            {
                edits.Add(Delete(file, WholeLine(import)));
                continue;
            }

            foreach (var node in group)
                edits.Add(
                    node is ImportSpecifier specifier && specifier.Parent is ImportDeclaration declaration
                        ? Delete(file, WithSeparator(declaration.Specifiers, specifier))
                        : Delete(file, WholeLine(node))
                );
        }

        return [Action("Remove unused imports", CodeActionKind.SourceOrganizeImports, uri, edits, [])];
    }

    /// <summary>
    ///     Every fix in the file that has only one way to be applied, as a single edit. A diagnostic offering
    ///     a choice - a name two modules export - is left out: picking one for the user is not fixing it.
    /// </summary>
    private static IEnumerable<CodeAction> FixAll(DocumentState state, DocumentUri uri, IReadOnlyDictionary<LoomDiagnostic, CodeAction[]> fixesByDiagnostic)
    {
        var fixable = fixesByDiagnostic.Where(entry => entry.Value.Length == 1).ToArray();
        if (fixable.Length == 0)
            return [];

        var taken = new List<TextSpan>();
        var edits = new List<TextEdit>();
        var fixedDiagnostics = new List<LoomDiagnostic>();
        foreach (var (diagnostic, fixes) in fixable)
        {
            var edit = fixes[0].Edit?.Changes?[uri].FirstOrDefault();
            if (edit == null || SpanOf(state.File.SourceFile, edit) is not { } span || Collides(taken, span))
                continue;

            taken.Add(span);
            edits.Add(edit);
            fixedDiagnostics.Add(diagnostic);
        }

        return edits.Count == 0 ? [] : [Action("Fix all auto-fixable problems", CodeActionKind.SourceFixAll, uri, edits, fixedDiagnostics)];
    }

    /// <summary>
    ///     Whether the edit cannot be applied alongside the ones already taken. Edits in one workspace edit
    ///     may not overlap, and two insertions at the same point are just as unapplicable as two overlapping
    ///     replacements - neither has an answer for which goes first.
    /// </summary>
    private static bool Collides(List<TextSpan> taken, TextSpan span) =>
        taken.Exists(other => span.Position == other.Position || span.Position < other.End && other.Position < span.End);

    private static TextSpan? SpanOf(SourceFile file, TextEdit edit)
    {
        try
        {
            var start = file.GetSourcePosition(edit.Range.Start.Character, edit.Range.Start.Line);
            return TextSpan.FromStartEnd(start, file.GetSourcePosition(edit.Range.End.Character, edit.Range.End.Line));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     The node the diagnostic was raised against, found by matching its span rather than by taking
    ///     whatever sits at its first character - an operator's diagnostic starts where its left operand
    ///     does, so the innermost node there is the operand rather than the operator.
    /// </summary>
    private static Node? NodeOf(LoomDiagnostic diagnostic, CompiledFile file)
    {
        var start = diagnostic.Span.Start.Position;
        var innermost = NodeFinder.FindAt(file.Tree, start);
        for (var node = innermost; node != null; node = node.Parent)
            if (node.Span.Position == start && node.Span.End == diagnostic.Span.End.Position)
                return node;

        return innermost;
    }

    /// <summary>
    ///     One fix per module exporting the missing name. The name comes from the source the diagnostic
    ///     underlines rather than from its message: the message is prose meant for a person to read.
    /// </summary>
    private static IEnumerable<CodeAction> ImportFixes(LoomDiagnostic diagnostic, DocumentState state, DocumentUri uri)
    {
        var name = diagnostic.Span.GetText().ToString();
        if (name.Length == 0)
            return [];

        return ImportCatalog.For(state.File.SourceFile, state.Unit, state.Modules)
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

        return [Fix($"Remove '{specifier.LocalName.Text}' from the import", uri, Delete(file, WithSeparator(import.Specifiers, specifier)), diagnostic)];
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
        Action(title, CodeActionKind.QuickFix, uri, [edit], [diagnostic]);

    private static CodeAction Action(
        string title,
        CodeActionKind kind,
        DocumentUri uri,
        IReadOnlyList<TextEdit> edits,
        IReadOnlyList<LoomDiagnostic> diagnostics) =>
        new()
        {
            Title = title,
            Kind = kind,
            Diagnostics = new Container<LspDiagnostic>(diagnostics.Select(Conversion.ToDiagnostic)),
            Edit = new WorkspaceEdit { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [uri] = edits } }
        };

    private static TextEdit Delete(SourceFile file, TextSpan span) => Replace(file, span, "");

    private static TextEdit Replace(SourceFile file, TextSpan span, string newText) =>
        new()
        {
            Range = new LspRange(
                Conversion.ToPosition(new Location(file, span.Position)),
                Conversion.ToPosition(new Location(file, span.End))
            ),
            NewText = newText
        };

    private static string TextOf(SourceFile file, TextSpan span) => file.SourceText.Substring(span.Position, span.Length);

    private static bool Overlaps(LoomDiagnostic diagnostic, TextSpan range) =>
        diagnostic.Span.Start.Position <= range.End && diagnostic.Span.End.Position >= range.Position;

    private static TextSpan WholeLine(Node node) => TextSpan.FromStartEnd(StartOfLine(node), EndOfLine(node));

    /// <summary>Removes one attribute from a list, the whole '[...]' when it was the only one written.</summary>
    private static TextEdit RemoveAttributeEdit(SourceFile file, Attributes attributes, LoomAttribute attribute) =>
        attributes.AttributeList.Count == 1
            ? Delete(file, WholeLine(attributes))
            : Delete(file, WithSeparator(attributes.AttributeList, attribute));

    /// <summary>
    ///     One item's span, widened to take its separator with it - the comma before it, or after when it was
    ///     first - so removing it does not leave <c>{ , b }</c> or <c>{ a, }</c> behind.
    /// </summary>
    private static TextSpan WithSeparator<T>(List<T> items, T item)
        where T : Node
    {
        var index = items.IndexOf(item);
        return index == 0
            ? TextSpan.FromStartEnd(item.Span.Position, items[1].Span.Position)
            : TextSpan.FromStartEnd(items[index - 1].Span.End, item.Span.End);
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
