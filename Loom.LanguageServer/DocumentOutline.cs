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
    /// <param name="describe">
    ///     Whether each entry carries the signature its declaration reads as. Off for a caller with nowhere
    ///     to show one: rendering a declaration means resolving and formatting its type, which across a whole
    ///     project is most of what building the outline costs, and the workspace symbol search that asks for
    ///     every file's outline on every keystroke has no field to put the answer in.
    /// </param>
    public static IReadOnlyList<DocumentSymbol> Of(CompiledFile file, bool describe = true) =>
        file.Tree.Statements
            .Select(statement => ToSymbol(statement, new Outline(file.SemanticModel, describe), LspSymbolKind.Function))
            .OfType<DocumentSymbol>()
            .ToArray();

    /// <summary>What every entry of one outline is built against: the file's own model, and how much to say about each name.</summary>
    private sealed record Outline(SemanticModel Model, bool Describe);

    /// <param name="functionKind">
    ///     What a function-shaped declaration is called in this position - a free <c>Function</c> at the top
    ///     level, a <c>Method</c> inside an interface, trait, or implementation.
    /// </param>
    private static DocumentSymbol? ToSymbol(Statement statement, Outline outline, LspSymbolKind functionKind)
    {
        switch (statement)
        {
            // 'export' and 'declare' wrap the declaration that carries the name; the outline entry keeps the
            // wrapper's range, so selecting it in the outline selects the whole statement as written
            case ExportDeclaration export:
                return Rerange(ToSymbol(export.Declaration, outline, functionKind), export);
            case Declare declare:
                return Rerange(ToSymbol(declare.Signature, outline, functionKind), declare);
            case InterfaceDeclaration @interface:
                return Build(@interface, @interface.Name, LspSymbolKind.Interface, outline, Members(@interface.Body?.Members, outline));
            case TraitDeclaration trait:
                return Build(trait, trait.Name, LspSymbolKind.Interface, outline, Members(trait.Body.Members, outline));
            case EnumDeclaration @enum:
                return Build(@enum, @enum.Name, LspSymbolKind.Enum, outline, @enum.Members.ConvertAll(member => EnumMemberSymbol(member, outline)));
            case Implement implement:
                return ImplementSymbol(implement, outline);
            case TypeAlias alias:
                return Build(alias, alias.Name, LspSymbolKind.Struct, outline, []);
            case EventDeclaration @event:
                return Build(@event, @event.Name, LspSymbolKind.Event, outline, []);
            case DeclareFunctionSignature function:
                return Build(function, function.Name, functionKind, outline, []);
            case DeclareVariableSignature variable:
                return Build(variable, variable.Name, VariableKind(variable), outline, []);
            case PropertyDeclaration property:
                return Build(property, property.Name, PropertyKind(property), outline, []);
            case IndexerDeclaration indexer:
                return IndexerSymbol(indexer);
            default:
                return null;
        }
    }

    private static List<DocumentSymbol> Members(IEnumerable<Statement>? members, Outline outline) =>
        members?.Select(member => ToSymbol(member, outline, LspSymbolKind.Method)).OfType<DocumentSymbol>().ToList() ?? [];

    private static DocumentSymbol ImplementSymbol(Implement implement, Outline outline) =>
        new()
        {
            Name = implement.TraitName.Name.Text,
            Detail = $"for {implement.InterfaceName.Name.Text}",
            Kind = LspSymbolKind.Interface,
            Range = Conversion.ToRange(implement.LocationSpan),
            SelectionRange = Conversion.ToRange(implement.TraitName.Name.GetLocation()),
            Children = new Container<DocumentSymbol>(Members(implement.Body.Implementations, outline))
        };

    private static DocumentSymbol EnumMemberSymbol(EnumMember member, Outline outline) =>
        new()
        {
            Name = member.Name.Text,
            Detail = ConstantText(member, outline),
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
        Outline outline,
        List<DocumentSymbol> children)
    {
        var symbol = outline.Model.GetDeclarationSymbol(declaration);
        return new DocumentSymbol
        {
            Name = name.Text,
            Detail = symbol == null || !outline.Describe ? "" : DeclarationDisplay.CompletionDetail(symbol, TypeOf(outline, declaration)),
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

    private static string ConstantText(EnumMember member, Outline outline) =>
        outline.Describe && TypeOf(outline, member) is { } type ? $" = {type}" : "";

    private static Type? TypeOf(Outline outline, Node node)
    {
        try
        {
            return outline.Model.GetType(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
