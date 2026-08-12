using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class NotPattern(Token notKeyword, Pattern pattern)
    : Pattern([notKeyword], [pattern])
{
    public Token NotKeyword { get; } = notKeyword;
    public Pattern Pattern { get; } = pattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNotPattern(this);
}
