using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ImplementBody(Token leftBrace, Token rightBrace, List<FunctionDeclaration> implementations)
    : Statement([leftBrace, rightBrace, ..implementations.SelectMany(m => m.Tokens)], implementations)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<FunctionDeclaration> Implementations { get; } = implementations;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitImplementBody(this);
}