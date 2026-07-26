using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ElseBranch(Token keyword, Statement branch)
    : Statement([keyword, ..branch.Tokens], [branch])
{
    public Token Keyword { get; } = keyword;
    public Statement Branch { get; } = branch;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitElseBranch(this);
}