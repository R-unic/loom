using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     A named function: a <c>fn</c> declaration, a <c>declare fn</c> signature, or a trait method made
///     visible inside an <c>implement</c> block.
/// </summary>
public sealed class FunctionSymbol(Node declaration, string name, List<AttributeSymbol>? attributes = null) : ValueSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Function;
    public override IReadOnlyList<AttributeSymbol> Attributes { get; } = attributes ?? [];
}
