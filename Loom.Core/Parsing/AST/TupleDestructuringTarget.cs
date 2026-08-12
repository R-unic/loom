using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TupleDestructuringTarget(Token leftParen, Token rightParen, List<DestructuringElement> elements)
    : DestructuringTarget([leftParen, rightParen], elements)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<DestructuringElement> Elements { get; } = elements;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTupleDestructuringTarget(this);
}
