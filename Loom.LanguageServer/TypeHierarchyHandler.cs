using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

namespace Loom.LanguageServer;

public sealed class TypeHierarchyPrepareHandler(DocumentStore documents) : TypeHierarchyPrepareHandlerBase
{
    public override Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchyPrepareParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<Container<TypeHierarchyItem>?>(null);

        var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
        var item = TypeHierarchy.At(state.File, offset) is { } symbol ? ToItem(symbol) : null;
        return Task.FromResult(item == null ? null : new Container<TypeHierarchyItem>(item));
    }

    internal static TypeHierarchyItem ToItem(TypeSymbol symbol) =>
        new()
        {
            Name = symbol.Name,
            // the protocol's SymbolKind has no case for a structural contract like a trait; Interface reads
            // the same way in every client's icon set, and is what an interface itself maps to as well
            Kind = LspSymbolKind.Interface,
            Uri = DocumentUri.FromFileSystemPath(symbol.File.AbsolutePath),
            Range = Conversion.ToRange(symbol.Declaration.LocationSpan),
            SelectionRange = Conversion.ToRange(NameOf(symbol).GetLocation()),
            Data = HierarchyData.Of(symbol)
        };

    private static Token NameOf(TypeSymbol symbol) => symbol.Declaration is NamedDeclaration named ? named.Name : symbol.Declaration.Tokens[0];

    protected override TypeHierarchyRegistrationOptions CreateRegistrationOptions(TypeHierarchyCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}

public sealed class TypeHierarchySupertypesHandler(DocumentStore documents) : TypeHierarchySupertypesHandlerBase
{
    public override Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySupertypesParams request, CancellationToken cancellationToken)
    {
        var symbol = HierarchyData.ResolveType(documents, request.Item.Data);
        if (symbol == null)
            return Task.FromResult<Container<TypeHierarchyItem>?>(null);

        var items = TypeHierarchy.Supertypes(symbol).Select(TypeHierarchyPrepareHandler.ToItem);
        return Task.FromResult<Container<TypeHierarchyItem>?>(new Container<TypeHierarchyItem>(items));
    }
}

public sealed class TypeHierarchySubtypesHandler(DocumentStore documents) : TypeHierarchySubtypesHandlerBase
{
    public override Task<Container<TypeHierarchyItem>?> Handle(TypeHierarchySubtypesParams request, CancellationToken cancellationToken)
    {
        var symbol = HierarchyData.ResolveType(documents, request.Item.Data);
        if (symbol == null || HierarchyData.StateOf(documents, request.Item.Uri) is not { } state)
            return Task.FromResult<Container<TypeHierarchyItem>?>(null);

        // Subtypes walks state.Unit.AnalyzedModules, which a concurrent recompile clears and repopulates
        IReadOnlyList<TypeSymbol> subtypes;
        lock (state.CompilationLock)
            subtypes = TypeHierarchy.Subtypes(symbol, state.Unit);

        var items = subtypes.Select(TypeHierarchyPrepareHandler.ToItem);
        return Task.FromResult<Container<TypeHierarchyItem>?>(new Container<TypeHierarchyItem>(items));
    }
}
