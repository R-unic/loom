using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class AndPattern(Pattern pattern, Token ampersand, Expression guard)
    : Pattern([ampersand], [pattern, guard])
{
    public Pattern Pattern { get; } = pattern;
    public Token Ampersand { get; } = ampersand;
    public Expression Guard { get; } = guard;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitAndPattern(this);
}
