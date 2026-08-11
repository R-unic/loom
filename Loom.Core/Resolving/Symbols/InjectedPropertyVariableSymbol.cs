using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

public sealed class InjectedPropertyVariableSymbol(Implement implement, string name, InterfaceSymbol from, bool isMutable = false)
    : ValueSymbol(implement, name, isMutable)
{
    public override SymbolKind Kind => SymbolKind.InjectedPropertyVariable;
    public InterfaceSymbol From { get; } = from;

    public override string ToString() => $"InjectedPropertyVariableSymbol({Name}, From: {From})";
}
