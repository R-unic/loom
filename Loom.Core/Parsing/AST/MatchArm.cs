using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class MatchArm(Pattern pattern, Token? when, Expression? guard, Token arrow, Expression body)
    : Node(
        [when, arrow, ..pattern.Tokens, ..guard?.Tokens ?? [], ..body.Tokens],
        [pattern, guard, body]
    )
{
    public Pattern Pattern { get; } = pattern;
    public Token? When { get; } = when;
    public Expression? Guard { get; } = guard;
    public Token Arrow { get; } = arrow;
    public Expression Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitMatchArm(this);
}