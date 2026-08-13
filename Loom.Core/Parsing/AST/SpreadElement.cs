using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class SpreadElement(Token dotDot, Expression expression)
    : Expression([dotDot], [expression])
{
    public Token DotDot { get; } = dotDot;
    public Expression Expression { get; } = expression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitSpreadElement(this);
}
