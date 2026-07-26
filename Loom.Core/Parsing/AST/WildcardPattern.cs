using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class WildcardPattern(Token token)
    : Pattern([token], [])
{
    public Token Token { get; } = token;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitWildcardPattern(this);
}