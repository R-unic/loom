using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Goes from a trait to the interfaces that implement it, or from an interface to the traits it
///     implements. A trait method's body is written somewhere its declaration never names, so the declaration
///     is the one place the reader can start from and the only place that knows the whole set.
/// </summary>
public sealed class ImplementationHandler(DocumentStore documents) : ImplementationHandlerBase
{
    public override Task<LocationOrLocationLinks?> Handle(ImplementationParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            var node = NodeFinder.FindAt(state.File.Tree, offset);
            if (node == null)
                return Task.FromResult<LocationOrLocationLinks?>(null);

            var locations = Implementations(node, offset, state)
                .Select(implemented => Conversion.ToLocation(implemented.LocationSpan))
                .OfType<Location>()
                .Select(location => new LocationOrLocationLink(location))
                .ToArray();

            return Task.FromResult<LocationOrLocationLinks?>(locations.Length == 0 ? null : new LocationOrLocationLinks(locations));
        }
        catch (Exception)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }
    }

    /// <summary>
    ///     What implements the declaration under the cursor. A trait method resolves to the function bodies
    ///     written for it; a trait or interface name resolves to the <c>implement</c> blocks joining the two.
    /// </summary>
    private static IEnumerable<Node> Implementations(Node node, int offset, DocumentState state)
    {
        if (MethodNameAt(node, offset) is { } methodName && node.FirstAncestorOfType<TraitDeclaration>() != null)
            return Blocks(state.Unit).SelectMany(block => block.Node.Body.Implementations).Where(function => function.Name.Text == methodName);

        // an interface declares a type and a value under one name, so the first symbol at the node is not
        // reliably the one carrying the type - every symbol there has to be considered
        var candidates = Candidates(node, offset, state).ToArray();
        if (candidates.OfType<TraitSymbol>().FirstOrDefault() is { } trait)
            return Blocks(state.Unit).Where(block => block.Model.GetSymbol(block.Node.TraitName) == trait).Select(block => (Node)block.Node);

        if (candidates.OfType<InterfaceSymbol>().FirstOrDefault() is { } @interface)
            return Blocks(state.Unit).Where(block => block.Model.GetSymbol(block.Node.InterfaceName) == @interface).Select(block => (Node)block.Node);

        return [];
    }

    private static IEnumerable<Symbol> Candidates(Node node, int offset, DocumentState state)
    {
        var semanticModel = state.File.SemanticModel;
        return semanticModel.GetDeclarationSymbols(node)
            .Concat(semanticModel.References.GetValueOrDefault(node.Id, []))
            .Concat(SymbolReferences.At(state.File, offset) is { } found ? [found] : []);
    }

    /// <summary>
    ///     Every <c>implement</c> block in the unit, since an interface may be extended from any module that
    ///     can see it. Each comes with the model of the file it was written in - the names it joins resolve
    ///     there, not in whichever file happens to be open.
    /// </summary>
    private static IEnumerable<(Implement Node, SemanticModel Model)> Blocks(CompilationUnit unit) =>
        unit.AnalyzedModules.Values.SelectMany(model => model.Tree.GetDescendants<Implement>().Select(block => (block, model)));

    /// <summary>The name of the trait method declaration the cursor is on, or null when it is not on one.</summary>
    private static string? MethodNameAt(Node node, int offset) =>
        node is DeclareFunctionSignature signature && signature.Name.Span.Contains(offset) ? signature.Name.Text : null;

    protected override ImplementationRegistrationOptions CreateRegistrationOptions(
        ImplementationCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
