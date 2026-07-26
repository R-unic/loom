using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class KeyOf(Token keyword, Token leftParen, Token rightParen, TypeExpression type)
    : TypeExpression([keyword, leftParen, ..type.Tokens, rightParen], [type])
{
    public Token Keyword { get; } = keyword;
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitKeyOf(this);
}