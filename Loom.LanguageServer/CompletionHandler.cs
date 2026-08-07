using System.Runtime.CompilerServices;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class CompletionHandler(DocumentStore documents) : CompletionHandlerBase
{
    private readonly ConditionalWeakTable<DocumentState, Symbol[]> _symbolCache = [];

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult(new CompletionList());

        var semanticModel = state.File.SemanticModel;
        var sourceFile = state.File.SourceFile;
        try
        {
            if (!_symbolCache.TryGetValue(state, out var symbols))
            {
                symbols = semanticModel.Declarations.Values
                    .SelectMany(list => list)
                    .Concat(state.Unit.Globals.Of(sourceFile).Keys)
                    .GroupBy(symbol => symbol.Name)
                    .Select(group => group.First())
                    .ToArray();

                _symbolCache.Add(state, symbols);
            }

            return Task.FromResult(GetCompletionListFromSymbols(symbols, semanticModel));
        }
        catch (Exception)
        {
            return Task.FromResult(new CompletionList());
        }
    }

    private static CompletionList GetCompletionListFromSymbols(IReadOnlyList<Symbol> symbols, SemanticModel semanticModel) =>
        new(symbols.Select(symbol => GetCompletionItemFromSymbol(symbol, semanticModel)));

    private static CompletionItem GetCompletionItemFromSymbol(Symbol symbol, SemanticModel semanticModel) =>
        new()
        {
            Label = symbol.Name,
            LabelDetails = new CompletionItemLabelDetails { Detail = ' ' + semanticModel.GetType(symbol.Declaration).ToString() },
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