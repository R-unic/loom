using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class NameOf(Token keyword, TypeArguments<TypeName>? typeArguments, Token leftParen, Token rightParen, Name? name)
    : Expression([keyword, leftParen, rightParen], [typeArguments, name])
{
    public Token Keyword { get; } = keyword;
    public TypeArguments<TypeName>? TypeArguments { get; } = typeArguments;
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public Name? Name { get; } = name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNameOf(this);
}