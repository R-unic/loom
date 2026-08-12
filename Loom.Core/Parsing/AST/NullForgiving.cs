using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class NullForgiving(Expression expression, Token bang)
    : Expression([bang], [expression])
{
    public Expression Expression { get; } = expression;
    public Token Bang { get; } = bang;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNullForgiving(this);
}
