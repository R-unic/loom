using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Every(Token keyword, Expression duration, Token? whileKeyword, Expression? condition, Statement body)
    : Statement([keyword, whileKeyword], [duration, condition, body])
{
    public Token Keyword { get; } = keyword;
    public Expression Duration { get; } = duration;
    public Token? WhileKeyword { get; } = whileKeyword;
    public Expression? Condition { get; } = condition;
    public Statement Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEvery(this);
}
