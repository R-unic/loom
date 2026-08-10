using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class CompletionHandler(DocumentStore documents) : CompletionHandlerBase
{
    private const int MaximumItems = 250;
    private const string UriKey = "loomUri";
    private const string OffsetKey = "loomOffset";
    private const string NameKey = "loomName";

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult(new CompletionList());

        var text = state.File.SourceFile.SourceText;
        var offset = IncrementalText.ToOffset(text, request.Position);
        var candidates = state.Completions.At(offset);
        var prefix = PrefixAt(text, offset);
        var matches = candidates
            .Where(symbol => symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(symbol => symbol.IsLocal)
            .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = matches.Take(MaximumItems).Select(symbol => ToCompletionItem(symbol, request.TextDocument.Uri, offset));
        return Task.FromResult(new CompletionList(items, matches.Length > MaximumItems));
    }

    /// <summary>
    ///     Fills in the signature and documentation for the one item the client is showing. Both are looked up
    ///     from the snapshot rather than carried in the item, so describing a name costs nothing until it is
    ///     the name being read - a project sees thousands of them per keystroke and reads at most one.
    /// </summary>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        if (Resolve(request) is not { } symbol)
            return Task.FromResult(request);

        var documentation = symbol.Documentation();
        return Task.FromResult(
            request with
            {
                LabelDetails = new CompletionItemLabelDetails { Detail = symbol.Detail() },
                Documentation = documentation == null
                    ? null
                    : new StringOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = documentation })
            }
        );
    }

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

    private VisibleSymbol? Resolve(CompletionItem item)
    {
        if (item.Data is not { } data
            || data[UriKey]?.Value<string>() is not { } uri
            || data[OffsetKey]?.Value<int>() is not { } offset
            || data[NameKey]?.Value<string>() is not { } name)
            return null;

        return documents.TryGetState(DocumentUri.Parse(uri), out var state) ? state.Completions.Find(offset, name) : null;
    }

    private static CompletionItem ToCompletionItem(VisibleSymbol symbol, DocumentUri uri, int offset) =>
        new()
        {
            Label = symbol.Name,
            Kind = symbol.Kind,
            // clients re-sort by their own match score and use sortText only to break ties, which is exactly
            // where a name the file itself declares should win over one of the thousands always in scope
            SortText = $"{(symbol.IsLocal ? '0' : '1')}{symbol.Name}",
            Data = new JObject { [UriKey] = uri.ToString(), [OffsetKey] = offset, [NameKey] = symbol.Name }
        };
}
