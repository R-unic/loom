using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

public sealed class ParameterSymbol(Node declaration, string name) : ValueSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Parameter;
}
