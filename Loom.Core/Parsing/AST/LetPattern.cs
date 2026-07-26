using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class LetPattern(Token keyword, Token name)
    : Pattern([keyword, name], [])
{
    public Token Keyword { get; } = keyword;
    public Token Name { get; } = name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitLetPattern(this);
}