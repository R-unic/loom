using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class RestPattern(Token dotDot, Pattern pattern)
    : Pattern([dotDot], [pattern])
{
    public Token DotDot { get; } = dotDot;
    public Pattern Pattern { get; } = pattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitRestPattern(this);
}