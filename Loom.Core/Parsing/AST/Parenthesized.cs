using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Parenthesized(Token leftParen, Token rightParen, Expression expression)
    : Expression([leftParen, rightParen], [expression])
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public Expression Expression { get; } = expression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitParenthesized(this);
}