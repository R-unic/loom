using System.Runtime.CompilerServices;
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
        
        try
        {
            var semanticModel = state.File.SemanticModel;
            if (!_symbolCache.TryGetValue(state, out var symbols))
            {
                symbols = semanticModel.Declarations.Values
                    .SelectMany(list => list)
                    .Concat(state.Unit.Globals.Of(state.File.SourceFile).Keys)
                    .GroupBy(symbol => symbol.Name)
                    .Select(group => group.First())
                    .ToArray();

                _symbolCache.Add(state, symbols);
            }
            
            var items = symbols
                .Select(symbol => new CompletionItem
                {
                    Label = symbol.Name,
                    LabelDetails = new CompletionItemLabelDetails
                    {
                        Detail = ' ' + semanticModel.GetType(symbol.Declaration).ToString()
                    },
                    Kind = ToCompletionItemKind(symbol.Kind)
                });

            return Task.FromResult(new CompletionList(items));
        }
        catch (Exception)
        {
            return Task.FromResult(new CompletionList());
        }
    }

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