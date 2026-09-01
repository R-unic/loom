using Loom.Core.Text;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Location = Loom.Core.Text.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

public sealed class CompletionHandler(DocumentStore documents) : CompletionHandlerBase
{
    private const int MaximumItems = 250;
    private const string UriKey = "loomUri";
    private const string OffsetKey = "loomOffset";
    private const string NameKey = "loomName";

    /// <summary>The replacement a module specifier's own completions need: `/` and `.` aren't identifier characters, so the normal word-prefix logic never covers them.</summary>
    private sealed record SpecifierReplacement(LspRange Range, string Prefix);

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult(new CompletionList());

        var text = state.File.SourceFile.SourceText;
        var offset = IncrementalText.ToOffset(text, request.Position);
        var candidates = state.Completions.At(offset);
        var replacement = SpecifierRangeAt(state.Completions.ModuleSpecifierRanges, offset) is { } range
            ? SpecifierReplacementAt(state.File.SourceFile, text, range, offset)
            : null;
        var prefix = replacement?.Prefix ?? PrefixAt(text, offset);
        var matches = candidates
            .Where(symbol => symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(symbol => symbol.IsLocal)
            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = matches.Take(MaximumItems).Select(symbol => ToCompletionItem(symbol, request.TextDocument.Uri, offset, replacement));
        return Task.FromResult(new CompletionList(items, matches.Length > MaximumItems));
    }

    private static TextSpan? SpecifierRangeAt(IReadOnlyList<TextSpan> ranges, int offset)
    {
        foreach (var range in ranges)
            if (range.Contains(offset))
                return range;

        return null;
    }

    /// <summary>
    ///     The already-written text from just past the opening quote to the cursor, and the range that
    ///     text occupies - both measured in the string's own content, since a specifier like
    ///     <c>./util/math</c> is written with characters (<c>.</c>, <c>/</c>) the identifier-prefix scan
    ///     would stop at.
    /// </summary>
    private static SpecifierReplacement SpecifierReplacementAt(SourceFile file, string text, TextSpan quoted, int offset)
    {
        var start = Math.Min(quoted.Position + 1, text.Length);
        var end = Math.Max(start, offset);
        return new SpecifierReplacement(
            new LspRange(Conversion.ToPosition(new Location(file, start)), Conversion.ToPosition(new Location(file, end))),
            text[start..end]
        );
    }

    /// <summary>
    ///     Fills in the signature and documentation for the one item the client is showing. Both are looked up
    ///     from the snapshot rather than carried in the item, so describing a name costs nothing until it is
    ///     the name being read - a project sees thousands of them per keystroke and reads at most one.
    /// </summary>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        if (Resolve(request) is not var (symbol, state))
            return Task.FromResult(request);

        // Documentation() is the closure CompletionSnapshotBuilder captured over state.Unit - built under
        // the lock, but invoked here, possibly long after and by a different request, so the read of
        // state.Unit.Roots it does through SymbolOrigin needs the lock again at the point it actually runs
        string? documentation;
        lock (state.CompilationLock)
            documentation = symbol.Documentation();

        return Task.FromResult(
            request with
            {
                LabelDetails = new CompletionItemLabelDetails { Detail = symbol.Detail(), Description = symbol.ImportedFrom },
                Documentation = documentation == null
                    ? null
                    : new StringOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = documentation }),
                AdditionalTextEdits = ImportEditFor(symbol, state)
            }
        );
    }

    /// <summary>
    ///     The import that has to be written for a name not yet in scope. It rides along with the completion
    ///     rather than being a separate step, so accepting the name leaves the file compiling.
    /// </summary>
    private static TextEditContainer? ImportEditFor(VisibleSymbol symbol, DocumentState state) =>
        symbol.ImportedFrom is { } specifier && ImportEdits.Add(state.File, symbol.Name, specifier) is { } edit
            ? new TextEditContainer(edit)
            : null;

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            ResolveProvider = true,
            TriggerCharacters = new Container<string>(".", "\"")
        };

    internal static string PrefixAt(string text, int offset)
    {
        var start = Math.Min(offset, text.Length);
        while (start > 0 && IsIdentifierCharacter(text[start - 1]))
            start--;

        return text[start..Math.Min(offset, text.Length)];
    }

    private static bool IsIdentifierCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private (VisibleSymbol Symbol, DocumentState State)? Resolve(CompletionItem item)
    {
        if (item.Data is not { } data
            || data[UriKey]?.Value<string>() is not { } uri
            || data[OffsetKey]?.Value<int>() is not { } offset
            || data[NameKey]?.Value<string>() is not { } name)
            return null;

        if (!documents.TryGetState(DocumentUri.Parse(uri), out var state))
            return null;

        return state.Completions.Find(offset, name) is { } symbol ? (symbol, state) : null;
    }

    private static CompletionItem ToCompletionItem(VisibleSymbol symbol, DocumentUri uri, int offset, SpecifierReplacement? replacement) =>
        new()
        {
            Label = symbol.Name,
            Kind = symbol.Kind,
            // clients re-sort by their own match score and use sortText only to break ties, which is exactly
            // where a name the file itself declares should win over one of the thousands always in scope -
            // and where a name that is not in scope at all should come last, since taking it edits the file
            SortText = $"{SortRank(symbol)}{symbol.Name}",
            // a client filters against this using the text already in the replaced range, and that range can
            // hold '/' and '.' - falling back to the label would drop every module specifier as soon as one
            // was typed
            FilterText = replacement != null ? symbol.Name : null,
            TextEdit = replacement is { } edit
                ? new TextEdit { Range = edit.Range, NewText = symbol.Name }
                : (TextEditOrInsertReplaceEdit?)null,
            Data = new JObject { [UriKey] = uri.ToString(), [OffsetKey] = offset, [NameKey] = symbol.Name }
        };

    private static char SortRank(VisibleSymbol symbol) => symbol.ImportedFrom != null ? '2' : symbol.IsLocal ? '0' : '1';
}
