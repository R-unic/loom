using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Is(Expression expression, Token keyword, Pattern pattern)
    : Expression([keyword], [expression, pattern])
{
    public Expression Expression { get; } = expression;
    public Token Keyword { get; } = keyword;
    public Pattern Pattern { get; } = pattern;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIs(this);
}
