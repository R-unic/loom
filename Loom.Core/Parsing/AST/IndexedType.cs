using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class IndexedType(Token leftBracket, Token rightBracket, TypeExpression targetType, TypeExpression indexType)
    : TypeExpression([..targetType.Tokens, leftBracket, ..indexType.Tokens, rightBracket], [targetType, indexType])
{
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public TypeExpression TargetType { get; } = targetType;
    public TypeExpression IndexType { get; } = indexType;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIndexedType(this);
}