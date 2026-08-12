namespace Loom.Core.Parsing.AST;

public class TypePattern(TypeExpression type, ObjectPattern? objectPattern)
    : Pattern([], [type, objectPattern])
{
    public TypeExpression Type { get; } = type;
    public ObjectPattern? ObjectPattern { get; } = objectPattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypePattern(this);
}