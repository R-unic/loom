using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class NullStatement(Token? token)
    : Statement([token], [])
{
    public Token? Token { get; } = token;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNullStatement(this);
}