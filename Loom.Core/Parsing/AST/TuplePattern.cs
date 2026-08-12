using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TuplePattern(Token leftParen, Token rightParen, List<Pattern> patterns)
    : Pattern([leftParen, rightParen], patterns)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<Pattern> Patterns { get; } = patterns;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTuplePattern(this);
}
