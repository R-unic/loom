using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ObjectPatternField(Token name, Token? colon, Pattern pattern)
    : Node([name, colon], [pattern])
{
    public Token Name { get; } = name;
    public Token? Colon { get; } = colon;
    public Pattern Pattern { get; } = pattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitObjectPatternField(this);
}