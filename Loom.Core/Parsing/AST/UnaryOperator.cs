using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class UnaryOperator(Token @operator, Expression operand)
    : Expression([@operator], [operand])
{
    public Token Operator { get; } = @operator;
    public Expression Operand { get; } = operand;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitUnaryOperator(this);
}