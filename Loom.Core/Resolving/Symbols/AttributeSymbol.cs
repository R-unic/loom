using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving.Symbols;

public class AttributeSymbol(Attribute attribute, string name)
    : Symbol(attribute, SymbolKind.Attribute, name)
{
    public Attribute Attribute { get; } = attribute;
}