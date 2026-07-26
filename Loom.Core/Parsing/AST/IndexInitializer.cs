using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class IndexInitializer(Token leftBracket, Token rightBracket, Token colon, Expression indexExpression, Expression expression)
    : InterfaceInvocationInitializer(expression, [leftBracket, rightBracket, colon, ..indexExpression.Tokens], [indexExpression])
{
    public Token RightBracket { get; } = rightBracket;
    public Token LeftBracket { get; } = leftBracket;
    public Token Colon { get; } = colon;
    public Expression IndexExpression { get; } = indexExpression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceInvocationIndexInitializer(this);
}