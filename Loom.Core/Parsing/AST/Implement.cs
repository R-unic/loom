using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class Implement(Token keyword, TypeName traitName, Token forKeyword, TypeName interfaceName, ImplementBody body)
    : Statement([keyword, traitName.Name, forKeyword, interfaceName.Name, ..body.Tokens], [traitName, interfaceName, body])
{
    public Token Keyword { get; } = keyword;
    public TypeName TraitName { get; } = traitName;
    public Token ForKeyword { get; } = forKeyword;
    public TypeName InterfaceName { get; } = interfaceName;
    public ImplementBody Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitImplement(this);
}