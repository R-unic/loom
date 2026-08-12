using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ArrayDestructuringTarget(Token leftBracket, Token rightBracket, List<DestructuringElement> elements)
    : DestructuringTarget([leftBracket, rightBracket], elements)
{
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public List<DestructuringElement> Elements { get; } = elements;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitArrayDestructuringTarget(this);
}
