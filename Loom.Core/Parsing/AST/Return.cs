using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Return(Token keyword, Expression? expression)
    : Statement([keyword, ..expression?.Tokens ?? []], [expression])
{
    public Token Keyword { get; } = keyword;
    public Expression? Expression { get; } = expression;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitReturn(this);
}