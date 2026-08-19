using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class WithOperator(Expression expression, Token keyword, InterfaceInvocationBody body)
    : Expression([keyword], [expression, body])
{
    public Expression Expression { get; } = expression;
    public Token Keyword { get; } = keyword;
    public InterfaceInvocationBody Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitWithOperator(this);
}
