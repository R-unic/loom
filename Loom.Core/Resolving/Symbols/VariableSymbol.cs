using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     A binding introduced by <c>let</c>/<c>mut</c>, a pattern, a namespace import, or the runtime half of
///     an interface or enum declaration.
/// </summary>
public sealed class VariableSymbol(Node declaration, string name, bool isMutable = false) : ValueSymbol(declaration, name, isMutable)
{
    public override SymbolKind Kind => SymbolKind.Variable;
}
