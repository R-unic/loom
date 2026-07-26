using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed class InjectedPropertyVariableSymbol(Implement implement, string name, InterfaceSymbol from, bool isMutable = false)
    : Symbol(implement, SymbolKind.InjectedPropertyVariable, name, isMutable)
{
    public InterfaceSymbol From { get; } = from;

    public override string ToString() => $"InjectedPropertyVariableSymbol({Name}, From: {From})";
}