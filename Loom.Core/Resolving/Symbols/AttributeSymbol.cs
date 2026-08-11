using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     An attribute written on a declaration. Neither a type nor a value: it is never looked up by name in
///     a scope, only read off the <see cref="Symbol.Attributes" /> of the declaration it annotates.
/// </summary>
public sealed class AttributeSymbol(Attribute attribute, string name) : Symbol(attribute, name)
{
    public override SymbolKind Kind => SymbolKind.Attribute;
    public Attribute Attribute { get; } = attribute;
}
