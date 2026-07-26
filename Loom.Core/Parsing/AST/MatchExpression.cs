using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class MatchExpression(Token keyword, Expression expression, Token leftBrace, Token rightBrace, List<MatchArm> arms)
    : Expression(
        [keyword, leftBrace, rightBrace, ..expression.Tokens, ..arms.SelectMany(a => a.Tokens)],
        [expression, ..arms]
    )
{
    public Token Keyword { get; } = keyword;
    public Expression Expression { get; } = expression;
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<MatchArm> Arms { get; } = arms;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitMatchExpression(this);
}