using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class If(Token keyword, Expression condition, Statement thenBranch, ElseBranch? elseBranch)
    : Statement([keyword, ..condition.Tokens, ..thenBranch.Tokens, ..elseBranch?.Tokens ?? []], [condition, thenBranch, elseBranch])
{
    public Token Keyword { get; } = keyword;
    public Expression Condition { get; } = condition;
    public Statement ThenBranch { get; } = thenBranch;
    public ElseBranch? ElseBranch { get; } = elseBranch;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIf(this);
}