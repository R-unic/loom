using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ArrayPattern(Token leftBracket, Token rightBracket, List<Pattern> elements, RestPattern? rest)
    : Pattern(
        [leftBracket, rightBracket, ..elements.SelectMany(e => e.Tokens), ..rest?.Tokens ?? []],
        [..elements, rest]
    )
{
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public List<Pattern> Elements { get; } = elements;
    public RestPattern? Rest { get; } = rest;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitArrayPattern(this);
}