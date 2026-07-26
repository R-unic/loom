using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class IdentifierPattern(Token name)
    : Pattern([name], [])
{
    public Token Name { get; } = name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIdentifierPattern(this);
}