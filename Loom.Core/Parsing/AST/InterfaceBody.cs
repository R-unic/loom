using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class InterfaceBody(Token leftBrace, Token rightBrace, List<Statement> members)
    : Statement([leftBrace, rightBrace, ..members.SelectMany(m => m.Tokens)], members)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<Statement> Members { get; } = members;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceBody(this);
}