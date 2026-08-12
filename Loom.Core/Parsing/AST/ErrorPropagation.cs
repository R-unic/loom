using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ErrorPropagation(Expression expression, Token question)
    : Expression([question], [expression])
{
    public Expression Expression { get; } = expression;
    public Token Question { get; } = question;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitErrorPropagation(this);
}
