using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ParenthesizedType(Token leftParen, Token rightParen, TypeExpression type)
    : TypeExpression([leftParen, rightParen], [type])
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitParenthesizedType(this);
}