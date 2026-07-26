using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class As(Token keyword, Expression expression, TypeExpression type)
    : Expression([..expression.Tokens, keyword, ..type.Tokens], [expression, type])
{
    public Token Keyword { get; } = keyword;
    public Expression Expression { get; } = expression;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitAs(this);
}