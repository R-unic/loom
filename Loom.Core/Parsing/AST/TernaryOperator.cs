using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TernaryOperator(Token question, Token colon, Expression condition, Expression thenBranch, Expression elseBranch)
    : Expression([question, colon], [condition, thenBranch, elseBranch])
{
    public Token Question { get; } = question;
    public Token Colon { get; } = colon;
    public Expression Condition { get; } = condition;
    public Expression ThenBranch { get; } = thenBranch;
    public Expression ElseBranch { get; } = elseBranch;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTernaryOperator(this);
}