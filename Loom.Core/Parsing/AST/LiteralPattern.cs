using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class LiteralPattern(Token token, object? value)
    : Pattern([token], [])
{
    public Token Token { get; } = token;
    public object? Value { get; } = value;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitLiteralPattern(this);
}