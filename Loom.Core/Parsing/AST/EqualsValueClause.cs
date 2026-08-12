using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class EqualsValueClause(Token equalsToken, Expression value)
    : Node([equalsToken], [value])
{
    public Token EqualsToken { get; } = equalsToken;
    public Expression Value { get; } = value;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEqualsValueClause(this);
}