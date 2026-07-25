using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class InterpolationHolePart(Token leftBrace, Expression expression, Token rightBrace) : InterpolationPart
{
    public Token LeftBrace { get; } = leftBrace;
    public Expression Expression { get; } = expression;
    public Token RightBrace { get; } = rightBrace;
}
