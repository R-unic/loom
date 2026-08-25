using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>A <c>type</c> alias, a type parameter, or an imported name whose module could not be resolved.</summary>
public sealed class TypeAliasSymbol(Node declaration, string name) : TypeSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Type;

    /// <summary>The alias's own <c>declare static</c> companion block, if it has declared one (at most one - see <see cref="Resolving.Resolver.VisitDeclareStaticBlock" />).</summary>
    public List<DeclareStaticBlock> StaticBlocks { get; } = [];
}
