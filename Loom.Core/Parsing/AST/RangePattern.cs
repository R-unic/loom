using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class RangePattern(Pattern minimum, Token dotDot, Pattern maximum)
    : Pattern([..minimum.Tokens, dotDot, ..maximum.Tokens], [minimum, maximum])
{
    public Pattern Minimum { get; } = minimum;
    public Token DotDot { get; } = dotDot;
    public Pattern Maximum { get; } = maximum;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitRangePattern(this);
}