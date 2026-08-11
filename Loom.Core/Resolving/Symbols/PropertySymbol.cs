using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     A member of an interface or trait body. <see cref="EventSymbol" /> refines it: an event is a member
///     of the same list, told apart by its <see cref="Symbol.Kind" />.
/// </summary>
public class PropertySymbol(NamedDeclaration declaration, InterfaceSymbol? pointsTo, List<AttributeSymbol> attributes)
    : ValueSymbol(declaration, declaration.Name.Text, declaration is PropertyDeclaration { MutKeyword: not null })
{
    public override SymbolKind Kind => SymbolKind.Property;

    /// <summary>The interface this member's declared type names, when it names one - the step a dotted member path walks through.</summary>
    public InterfaceSymbol? PointsTo { get; } = pointsTo;
    public override IReadOnlyList<AttributeSymbol> Attributes { get; } = attributes;
}
