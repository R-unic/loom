using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypedPattern(Token name, Token when, TypeExpression type, ObjectPattern? objectPattern)
    : Pattern([name, when], [type, objectPattern])
{
    public Token Name { get; } = name;
    public Token When { get; } = when;
    public TypeExpression Type { get; } = type;
    public ObjectPattern? ObjectPattern { get; } = objectPattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypedPattern(this);
}