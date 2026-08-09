using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class CompletionHandler(DocumentStore documents) : CompletionHandlerBase
{
    private const int MaximumItems = 250;
    private const string DetailKey = "loomDetail";

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

        return Task.FromResult(new CompletionList(matches.Take(MaximumItems).Select(ToCompletionItem), matches.Length > MaximumItems));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Data?[DetailKey]?.Value<string>() is { } detail
            ? request with { LabelDetails = new CompletionItemLabelDetails { Detail = ' ' + detail } }
            : request
        );

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

    private static CompletionItem ToCompletionItem(VisibleSymbol symbol) =>
        new()
        {
            Label = symbol.Name,
            Kind = symbol.Kind,
            Data = new JObject { [DetailKey] = symbol.TypeDescription }
        };
}
