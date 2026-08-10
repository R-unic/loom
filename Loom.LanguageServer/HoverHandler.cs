using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

public sealed class HoverHandler(DocumentStore documents) : HoverHandlerBase
{
    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<Hover?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            var node = NodeFinder.FindAt(state.File.Tree, offset);
            if (node == null || Describe(node, offset, state) is not { } markdown)
                return Task.FromResult<Hover?>(null);

            var hover = new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown }),
                Range = Conversion.ToRange(node.LocationSpan)
            };

            return Task.FromResult<Hover?>(hover);
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<Hover?>(null);
        }
    }

    /// <summary>
    ///     What to say about the node under the cursor. A name that resolves to a symbol is described through
    ///     its declaration, which carries the keyword, parameter names, attributes and prose that the type
    ///     alone throws away; anything else falls back to its type, which is all there is to say about it.
    /// </summary>
    private static string? Describe(Node node, int offset, DocumentState state)
    {
        var semanticModel = state.File.SemanticModel;
        if (DescribeMemberAt(node, offset, state) is { } member)
            return member;

        if (SymbolAt(node, semanticModel) is { } symbol)
            return SymbolMarkdown.Describe(symbol, TypeOf(semanticModel, symbol.Declaration) ?? TypeOf(semanticModel, node), state.File.SourceFile, state.Unit, state.Modules);

        var type = TypeOf(semanticModel, node);
        return type == null ? null : $"```loom\n{type}\n```";
    }

    private static Symbol? SymbolAt(Node node, SemanticModel semanticModel) =>
        semanticModel.GetSymbol(node) ?? semanticModel.GetDeclarationSymbol(node);

    /// <summary>
    ///     The member named at <paramref name="offset" /> in a dotted chain. <see cref="DotName" /> is not a
    ///     node, so nothing in the tree stands for a single link of the chain - the receiver's type has to be
    ///     walked forward one name at a time until the one the cursor is on.
    /// </summary>
    private static string? DescribeMemberAt(Node node, int offset, DocumentState state)
    {
        var (receiver, names) = node switch
        {
            QualifiedName qualified => ((Node?)qualified.Identifier, qualified.Names),
            PropertyAccess propertyAccess => (propertyAccess.Expression, propertyAccess.Names),
            _ => (null, null)
        };

        if (receiver == null || names == null)
            return null;

        var semanticModel = state.File.SemanticModel;
        var receiverType = TypeOf(semanticModel, receiver);
        foreach (var dotName in names)
        {
            var name = dotName.Name.Text;
            var memberType = TypeMembers.PropertyType(receiverType, name);
            if (!TextSpan.FromStartEnd(dotName.Dot.Span.End, dotName.Name.Span.End).Contains(offset))
            {
                receiverType = memberType;
                continue;
            }

            if (memberType == null)
                return null;

            // a member of a declared interface has a symbol of its own, carrying the attributes and prose
            // its declaration was written with; one of a structural type has only its name and type
            var property = receiverType == null ? null : semanticModel.GetPropertySymbol(receiverType, [name]);
            return property == null
                ? SymbolMarkdown.DescribeMember(name, memberType)
                : SymbolMarkdown.Describe(property, memberType, state.File.SourceFile, state.Unit, state.Modules);
        }

        return null;
    }

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            var type = semanticModel.GetType(node);
            return type is TypeVariable ? null : type;
        }
        catch (Exception)
        {
            return null;
        }
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
