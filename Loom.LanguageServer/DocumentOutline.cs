using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using AstFunctionType = Loom.Core.Parsing.AST.FunctionType;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>
///     The file's declarations as a tree, for the outline view, breadcrumbs and go-to-symbol. Built from the
///     syntax rather than from the symbol table so that a file which does not fully resolve still has an
///     outline - the shape of a file is the one thing an editor should keep showing while it is being edited.
/// </summary>
public static class DocumentOutline
{
    public static IReadOnlyList<DocumentSymbol> Of(CompiledFile file) =>
        file.Tree.Statements.Select(statement => ToSymbol(statement, file.SemanticModel, LspSymbolKind.Function)).OfType<DocumentSymbol>().ToArray();

    /// <param name="functionKind">
    ///     What a function-shaped declaration is called in this position - a free <c>Function</c> at the top
    ///     level, a <c>Method</c> inside an interface, trait, or implementation.
    /// </param>
    private static DocumentSymbol? ToSymbol(Statement statement, SemanticModel semanticModel, LspSymbolKind functionKind)
    {
        switch (statement)
        {
            // 'export' and 'declare' wrap the declaration that carries the name; the outline entry keeps the
            // wrapper's range, so selecting it in the outline selects the whole statement as written
            case ExportDeclaration export:
                return Rerange(ToSymbol(export.Declaration, semanticModel, functionKind), export);
            case Declare declare:
                return Rerange(ToSymbol(declare.Signature, semanticModel, functionKind), declare);
            case InterfaceDeclaration @interface:
                return Build(@interface, @interface.Name, LspSymbolKind.Interface, semanticModel, Members(@interface.Body?.Members, semanticModel));
            case TraitDeclaration trait:
                return Build(trait, trait.Name, LspSymbolKind.Interface, semanticModel, Members(trait.Body.Members, semanticModel));
            case EnumDeclaration @enum:
                return Build(@enum, @enum.Name, LspSymbolKind.Enum, semanticModel, @enum.Members.ConvertAll(member => EnumMemberSymbol(member, semanticModel)));
            case Implement implement:
                return ImplementSymbol(implement, semanticModel);
            case TypeAlias alias:
                return Build(alias, alias.Name, LspSymbolKind.Struct, semanticModel, []);
            case EventDeclaration @event:
                return Build(@event, @event.Name, LspSymbolKind.Event, semanticModel, []);
            case DeclareFunctionSignature function:
                return Build(function, function.Name, functionKind, semanticModel, []);
            case DeclareVariableSignature variable:
                return Build(variable, variable.Name, VariableKind(variable), semanticModel, []);
            case PropertyDeclaration property:
                return Build(property, property.Name, PropertyKind(property), semanticModel, []);
            case IndexerDeclaration indexer:
                return IndexerSymbol(indexer);
            default:
                return null;
        }
    }

    private static List<DocumentSymbol> Members(IEnumerable<Statement>? members, SemanticModel semanticModel) =>
        members?.Select(member => ToSymbol(member, semanticModel, LspSymbolKind.Method)).OfType<DocumentSymbol>().ToList() ?? [];

    private static DocumentSymbol ImplementSymbol(Implement implement, SemanticModel semanticModel) =>
        new()
        {
            Name = implement.TraitName.Name.Text,
            Detail = $"for {implement.InterfaceName.Name.Text}",
            Kind = LspSymbolKind.Interface,
            Range = Conversion.ToRange(implement.LocationSpan),
            SelectionRange = Conversion.ToRange(implement.TraitName.Name.GetLocation()),
            Children = new Container<DocumentSymbol>(Members(implement.Body.Implementations, semanticModel))
        };

    private static DocumentSymbol EnumMemberSymbol(EnumMember member, SemanticModel semanticModel) =>
        new()
        {
            Name = member.Name.Text,
            Detail = ConstantText(member, semanticModel),
            Kind = LspSymbolKind.EnumMember,
            Range = Conversion.ToRange(member.LocationSpan),
            SelectionRange = Conversion.ToRange(member.Name.GetLocation())
        };

    /// <summary>An indexer has no name of its own, so the outline shows the syntax that declared it.</summary>
    private static DocumentSymbol IndexerSymbol(IndexerDeclaration indexer) =>
        new()
        {
            Name = $"[{indexer.IndexType}]",
            Detail = $": {indexer.ColonTypeClause.Type}",
            Kind = LspSymbolKind.Key,
            Range = Conversion.ToRange(indexer.LocationSpan),
            SelectionRange = Conversion.ToRange(indexer.LocationSpan)
        };

    private static DocumentSymbol Build(
        Node declaration,
        Token name,
        LspSymbolKind kind,
        SemanticModel semanticModel,
        List<DocumentSymbol> children)
    {
        var symbol = semanticModel.GetDeclarationSymbol(declaration);
        return new DocumentSymbol
        {
            Name = name.Text,
            Detail = symbol == null ? "" : DeclarationDisplay.CompletionDetail(symbol, TypeOf(semanticModel, declaration)),
            Kind = kind,
            Tags = symbol != null && DeclarationDisplay.DeprecationOf(symbol) != null ? new Container<SymbolTag>(SymbolTag.Deprecated) : null,
            Range = Conversion.ToRange(declaration.LocationSpan),
            SelectionRange = Conversion.ToRange(name.GetLocation()),
            Children = children.Count == 0 ? null : new Container<DocumentSymbol>(children)
        };
    }

    /// <summary>
    ///     Widens an entry's range to the statement that wrapped it. The protocol requires the selection range
    ///     to sit inside the range, and it already does: the wrapper only ever adds a keyword in front.
    /// </summary>
    private static DocumentSymbol? Rerange(DocumentSymbol? symbol, Node wrapper) =>
        symbol == null ? null : symbol with { Range = Conversion.ToRange(wrapper.LocationSpan) };

    private static LspSymbolKind VariableKind(DeclareVariableSignature variable) =>
        variable.Keyword.Text == "mut" ? LspSymbolKind.Variable : LspSymbolKind.Constant;

    private static LspSymbolKind PropertyKind(PropertyDeclaration property) =>
        property.ColonTypeClause.Type is AstFunctionType ? LspSymbolKind.Method : LspSymbolKind.Property;

    private static string ConstantText(EnumMember member, SemanticModel semanticModel) =>
        TypeOf(semanticModel, member) is { } type ? $" = {type}" : "";

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            return semanticModel.GetType(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
