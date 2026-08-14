using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

/// <summary>
///     Where a hierarchy item points back into the source, carried in <see cref="CallHierarchyItem.Data" /> so
///     that the next request in the chain - an incoming or outgoing call - can re-find the symbol without a
///     text position of its own to work from.
/// </summary>
internal static class HierarchyData
{
    private const string UriKey = "loomUri";
    private const string OffsetKey = "loomOffset";
    private const string NameKey = "loomName";

    public static JObject Of(Symbol symbol) =>
        new()
        {
            [UriKey] = DocumentUri.FromFileSystemPath(symbol.File.AbsolutePath).ToString(),
            [OffsetKey] = NameOf(symbol.Declaration).Span.Position,
            [NameKey] = symbol.Name
        };

    /// <summary>
    ///     Uses the same resolution <see cref="CallHierarchy.At" />/<see cref="TypeHierarchy.At" /> apply to a
    ///     live cursor position, rather than <see cref="SymbolReferences.At" /> directly - a declaration that
    ///     names a type and a value under one node needs the same disambiguation here as it does there, and
    ///     re-deriving it a third time is how the two copies would drift.
    /// </summary>
    public static FunctionSymbol? ResolveFunction(DocumentStore documents, JToken? data) => With(documents, data, CallHierarchy.At);

    public static TypeSymbol? ResolveType(DocumentStore documents, JToken? data) => With(documents, data, TypeHierarchy.At);

    /// <remarks>
    ///     A tree view built from hierarchy items stays open and gets expanded node by node while the file
    ///     keeps being edited, so the offset a node was prepared with can drift arbitrarily far from what it
    ///     named. The name travels alongside it for exactly that reason: whatever now resolves at the offset
    ///     is trusted only if it is still called what the item said it was, the same guard
    ///     <see cref="CodeLensHandler" /> keeps for the same reason.
    /// </remarks>
    private static T? With<T>(DocumentStore documents, JToken? data, Func<Core.Pipeline.CompiledFile, int, T?> at)
        where T : Symbol
    {
        if (data is not JObject @object
            || @object[UriKey]?.Value<string>() is not { } uri
            || @object[OffsetKey]?.Value<int>() is not { } offset
            || @object[NameKey]?.Value<string>() is not { } name)
            return null;

        if (!documents.TryGetState(DocumentUri.Parse(uri), out var state))
            return null;

        var resolved = at(state.File, offset);
        return resolved != null && resolved.Name == name ? resolved : null;
    }

    /// <summary>The unit an item's own file belongs to - every hierarchy item names its file, so this needs nothing else.</summary>
    public static Core.Pipeline.CompilationUnit? UnitOf(DocumentStore documents, DocumentUri uri) =>
        documents.TryGetState(uri, out var state) ? state.Unit : null;

    private static Token NameOf(Node declaration) => declaration is NamedDeclaration named ? named.Name : declaration.Tokens[0];
}

public sealed class CallHierarchyPrepareHandler(DocumentStore documents) : CallHierarchyPrepareHandlerBase
{
    public override Task<Container<CallHierarchyItem>?> Handle(CallHierarchyPrepareParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<Container<CallHierarchyItem>?>(null);

        var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
        var item = CallHierarchy.At(state.File, offset) is { } symbol ? ToItem(symbol) : null;
        return Task.FromResult<Container<CallHierarchyItem>?>(item == null ? null : new Container<CallHierarchyItem>(item));
    }

    internal static CallHierarchyItem ToItem(FunctionSymbol symbol) =>
        new()
        {
            Name = symbol.Name,
            Kind = CallHierarchy.IsMethod(symbol.Declaration) ? LspSymbolKind.Method : LspSymbolKind.Function,
            Uri = DocumentUri.FromFileSystemPath(symbol.File.AbsolutePath),
            Range = Conversion.ToRange(symbol.Declaration.LocationSpan),
            SelectionRange = Conversion.ToRange(NameOf(symbol).GetLocation()),
            Data = HierarchyData.Of(symbol)
        };

    private static Token NameOf(FunctionSymbol symbol) => symbol.Declaration is NamedDeclaration named ? named.Name : symbol.Declaration.Tokens[0];

    protected override CallHierarchyRegistrationOptions CreateRegistrationOptions(CallHierarchyCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}

/// <summary>
///     What both call-direction handlers share: resolve the item back to a symbol and a unit, ask
///     <see cref="CallHierarchy" /> for the edges, and wrap each in whichever record the request wants.
/// </summary>
/// <remarks>
///     No dedicated handler base exists for either of these requests in the client library, only for
///     <c>prepareCallHierarchy</c> - the params types already carry their own <c>[Method]</c> attribute and
///     cover registration under the one capability <see cref="CallHierarchyPrepareHandler" /> advertises.
/// </remarks>
internal static class CallHierarchyCalls
{
    public static Task<Container<TCall>?> HandleAsync<TCall>(
        DocumentStore documents,
        CallHierarchyItem item,
        Func<FunctionSymbol, Core.Pipeline.CompilationUnit, IReadOnlyList<CallEdge>> edgesOf,
        Func<CallEdge, TCall> toCall)
    {
        if (HierarchyData.ResolveFunction(documents, item.Data) is not { } symbol || HierarchyData.UnitOf(documents, item.Uri) is not { } unit)
            return Task.FromResult<Container<TCall>?>(null);

        return Task.FromResult<Container<TCall>?>(new Container<TCall>(edgesOf(symbol, unit).Select(toCall)));
    }

    public static Container<Range> RangesOf(CallEdge edge) => new(edge.CallSites.Select(token => Conversion.ToRange(token.GetLocation())));
}

public sealed class CallHierarchyIncomingCallsHandler(DocumentStore documents)
    : IJsonRpcRequestHandler<CallHierarchyIncomingCallsParams, Container<CallHierarchyIncomingCall>?>
{
    public Task<Container<CallHierarchyIncomingCall>?> Handle(CallHierarchyIncomingCallsParams request, CancellationToken cancellationToken) =>
        CallHierarchyCalls.HandleAsync(
            documents,
            request.Item,
            (symbol, unit) => CallHierarchy.IncomingCalls(symbol, unit, cancellationToken),
            edge => new CallHierarchyIncomingCall { From = CallHierarchyPrepareHandler.ToItem(edge.Symbol), FromRanges = CallHierarchyCalls.RangesOf(edge) }
        );
}

public sealed class CallHierarchyOutgoingCallsHandler(DocumentStore documents)
    : IJsonRpcRequestHandler<CallHierarchyOutgoingCallsParams, Container<CallHierarchyOutgoingCall>?>
{
    public Task<Container<CallHierarchyOutgoingCall>?> Handle(CallHierarchyOutgoingCallsParams request, CancellationToken cancellationToken) =>
        CallHierarchyCalls.HandleAsync(
            documents,
            request.Item,
            (symbol, unit) => CallHierarchy.OutgoingCalls(symbol, unit),
            edge => new CallHierarchyOutgoingCall { To = CallHierarchyPrepareHandler.ToItem(edge.Symbol), FromRanges = CallHierarchyCalls.RangesOf(edge) }
        );
}
