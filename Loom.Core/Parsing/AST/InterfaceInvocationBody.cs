using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class InterfaceInvocationBody(Token leftBrace, Token rightBrace, List<InterfaceInvocationInitializer> initializers)
    : Expression([leftBrace, rightBrace, ..initializers.SelectMany(i => i.Tokens)], initializers)
{
    public List<InterfaceInvocationInitializer> Initializers { get; } = initializers;
    public Token RightBrace { get; } = rightBrace;
    public Token LeftBrace { get; } = leftBrace;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceInvocationBody(this);
}