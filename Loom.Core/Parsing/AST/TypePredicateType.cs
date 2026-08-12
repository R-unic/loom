using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class TypePredicateType(Expression subject, Token isKeyword, TypeExpression type)
    : TypeExpression([isKeyword], [subject, type])
{
    public Expression Subject { get; } = subject;
    public Token IsKeyword { get; } = isKeyword;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypePredicateType(this);
}
