using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class BinaryOperator(Token @operator, Expression left, Expression right)
    : Expression([@operator], [left, right])
{
    public Token Operator { get; } = @operator;
    public Expression Left { get; } = left;
    public Expression Right { get; } = right;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitBinaryOperator(this);
}