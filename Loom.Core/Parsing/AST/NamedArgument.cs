using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class NamedArgument(Token name, Token colon, Expression value)
    : Expression([name, colon], [value])
{
    public Token Name { get; } = name;
    public Token Colon { get; } = colon;
    public Expression Value { get; } = value;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNamedArgument(this);
}
