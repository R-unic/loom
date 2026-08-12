using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class OptionalType(Token question, TypeExpression nonNullableType)
    : TypeExpression([question], [nonNullableType])
{
    public Token Question { get; } = question;
    public TypeExpression NonNullableType { get; } = nonNullableType;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitOptionalType(this);
}