using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ArrayType(TypeExpression elementType, Token leftBracket, Token? mutKeyword, Token rightBracket)
    : TypeExpression([mutKeyword, leftBracket, mutKeyword, rightBracket], [elementType])
{
    public TypeExpression ElementType { get; } = elementType;
    public Token LeftBracket { get; } = leftBracket;
    public Token? MutKeyword { get; } = mutKeyword;
    public Token RightBracket { get; } = rightBracket;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitArrayType(this);
}