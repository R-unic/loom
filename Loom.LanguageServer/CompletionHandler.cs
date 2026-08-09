using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class CompletionHandler(DocumentStore documents) : CompletionHandlerBase
{
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult(new CompletionList());

        return Task.FromResult(new CompletionList(state.VisibleSymbols.Select(GetCompletionItemFromSymbol)));
    }

    private static CompletionItem GetCompletionItemFromSymbol(VisibleSymbol symbol) =>
        new()
        {
            Label = symbol.Name,
            LabelDetails = new CompletionItemLabelDetails { Detail = ' ' + symbol.TypeDescription },
            Kind = ToCompletionItemKind(symbol.Kind)
        };

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), ResolveProvider = false };

    private static CompletionItemKind ToCompletionItemKind(LoomSymbolKind kind) =>
        kind switch
        {
            LoomSymbolKind.Function => CompletionItemKind.Function,
            LoomSymbolKind.Variable or LoomSymbolKind.Parameter => CompletionItemKind.Variable,
            LoomSymbolKind.Property => CompletionItemKind.Property,
            LoomSymbolKind.InjectedPropertyVariable => CompletionItemKind.Property,
            LoomSymbolKind.Type => CompletionItemKind.Class,
            LoomSymbolKind.EnumType => CompletionItemKind.Enum,
            LoomSymbolKind.Interface or LoomSymbolKind.Trait => CompletionItemKind.Interface,
            LoomSymbolKind.Event => CompletionItemKind.Event,
            LoomSymbolKind.Attribute => CompletionItemKind.Function,
            _ => CompletionItemKind.Text
        };
}
