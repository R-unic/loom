using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

public sealed class TraitSymbol(TraitDeclaration declaration, string name)
    : TypeSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Trait;
    public IReadOnlySet<string> MethodNames { get; } = declaration.Body.Members.Select(sig => sig.Name.Text).ToHashSet();
    public List<InterfaceSymbol> ImplementedBy { get; } = [];

    /// <summary>Method name to its default body, for a member declared with one - an 'implement' block may omit it.</summary>
    public IReadOnlyDictionary<string, FunctionDeclaration> Defaults { get; } =
        declaration.Body.Members.OfType<FunctionDeclaration>().ToDictionary(m => m.Name.Text, m => m);

    /// <summary>Metamethod name (e.g. "__add") to trait method name, for members tagged with [luau_metamethod(...)].</summary>
    public IReadOnlyDictionary<string, string> Metamethods { get; } =
        MetamethodAttributes.Collect(declaration.Body.Members, m => m.Name.Text, m => m.Attributes);

    public override string ToString() => $"TraitSymbol({Name}, ImplementedBy: [{string.Join(", ", ImplementedBy.Select(s => s.Name))}])";
}