using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>An enum's type. Its runtime table is a separate <see cref="VariableSymbol" /> on the same declaration.</summary>
public sealed class EnumTypeSymbol(Node declaration, string name) : TypeSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.EnumType;
}
