using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class RangeLiteral(Token dotDot, Expression minimum, Expression maximum)
    : Expression([..minimum.Tokens, dotDot, ..maximum.Tokens], [minimum, maximum])
{
    public Token DotDot { get; } = dotDot;
    public Expression Minimum { get; } = minimum;
    public Expression Maximum { get; } = maximum;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitRangeLiteral(this);
}