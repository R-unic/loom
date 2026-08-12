using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Block(Token leftBrace, Token rightBrace, List<Statement> statements)
    : Statement([leftBrace, rightBrace], statements)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<Statement> Statements { get; } = statements;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitBlock(this);
}