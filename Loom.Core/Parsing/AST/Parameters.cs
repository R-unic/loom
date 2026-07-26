using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Parameters(Token leftParen, Token rightParen, List<Parameter> parameters)
    : Statement([leftParen, ..parameters.SelectMany(p => p.Tokens), rightParen], parameters)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<Parameter> ParameterList { get; } = parameters;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitParameters(this);
}