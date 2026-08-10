using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.LanguageServer;

/// <summary>
///     Goes from a value to the declaration of the type it has, rather than to where the value itself came
///     from. The one place this differs most from go-to-definition is the case it exists for: on a binding
///     with no annotation, its type was never written down anywhere the cursor could reach.
/// </summary>
public sealed class TypeDefinitionHandler(DocumentStore documents) : TypeDefinitionHandlerBase
{
    private const int MaximumDepth = 8;

    public override Task<LocationOrLocationLinks?> Handle(TypeDefinitionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            var node = NodeFinder.FindAt(state.File.Tree, offset);
            if (node == null || TypeOf(state.File.SemanticModel, node) is not { } type)
                return Task.FromResult<LocationOrLocationLinks?>(null);

            var locations = NamesOf(type, 0)
                .Distinct()
                .Select(state.File.SemanticModel.FindTypeDeclaration)
                .OfType<Loom.Core.Resolving.Symbols.Symbol>()
                .Select(symbol => Conversion.ToLocation(symbol.Declaration.LocationSpan))
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
    ///     The named types a value's type is built from. A wrapper is looked through rather than reported -
    ///     what the reader wants from <c>Player?</c> or <c>Player[]</c> is <c>Player</c> - and a union offers
    ///     every arm, since each is somewhere the value might have come from.
    /// </summary>
    private static IEnumerable<string> NamesOf(Type type, int depth)
    {
        if (depth >= MaximumDepth)
            return [];

        return type switch
        {
            InterfaceType @interface => [@interface.Name],
            InstantiatedType instantiated => NamesOf(instantiated.Expand(), depth + 1),
            OptionalType optional => NamesOf(optional.NonNullableType, depth + 1),
            ArrayType array => NamesOf(array.ElementType, depth + 1),
            UnionType union => union.Types.SelectMany(member => NamesOf(member, depth + 1)),
            IntersectionType intersection => intersection.Types.SelectMany(member => NamesOf(member, depth + 1)),
            TypeParameter parameter => parameter.Constraint == null ? [] : NamesOf(parameter.Constraint, depth + 1),
            _ => []
        };
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

    protected override TypeDefinitionRegistrationOptions CreateRegistrationOptions(
        TypeDefinitionCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
