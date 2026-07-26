using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Arguments(Token leftParen, Token rightParen, List<Expression> arguments)
    : Expression([leftParen, ..arguments.SelectMany(p => p.Tokens), rightParen], arguments)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<Expression> ArgumentList { get; } = arguments;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitArguments(this);
}