using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ElementAccess(Token leftBracket, Token rightBracket, Expression expression, Expression indexExpression)
    : AssignmentTarget([..expression.Tokens, leftBracket, ..indexExpression.Tokens, rightBracket], [expression, indexExpression])
{
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public Expression Expression { get; } = expression;
    public Expression IndexExpression { get; } = indexExpression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitElementAccess(this);
}