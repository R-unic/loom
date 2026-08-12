using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class EqualsTypeClause(Token equalsToken, TypeExpression type)
    : Node([equalsToken], [type])
{
    public Token EqualsToken { get; } = equalsToken;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEqualsTypeClause(this);
}