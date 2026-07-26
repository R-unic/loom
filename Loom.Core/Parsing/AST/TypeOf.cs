using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypeOf(Token keyword, Token leftParen, Token rightParen, Expression expression)
    : TypeExpression([keyword, leftParen, ..expression.Tokens, rightParen], [expression])
{
    public Token Keyword { get; } = keyword;
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public Expression Expression { get; } = expression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeOf(this);
}