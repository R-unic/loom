using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ExpressionBody(Token arrow, Expression expression)
    : Statement([arrow, ..expression.Tokens], [expression])
{
    public Token Arrow { get; } = arrow;
    public Expression Expression { get; } = expression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExpressionBody(this);
}