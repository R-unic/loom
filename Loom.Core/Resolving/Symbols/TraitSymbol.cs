using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed class TraitSymbol(TraitDeclaration declaration, string name)
    : Symbol(declaration, SymbolKind.Trait, name)
{
    public IReadOnlySet<string> MethodNames { get; } = declaration.Body.Members.Select(sig => sig.Name.Text).ToHashSet();
    public List<InterfaceSymbol> ImplementedBy { get; } = [];

    public override string ToString() => $"TraitSymbol({Name}, ImplementedBy: [{string.Join(", ", ImplementedBy.Select(s => s.Name))}])";
}