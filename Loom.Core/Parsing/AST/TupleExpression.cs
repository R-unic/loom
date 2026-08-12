using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TupleExpression(Token leftParen, Token rightParen, List<Token> commas, List<Expression> expressions)
    : Expression([leftParen, rightParen, ..commas], expressions)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<Token> Commas { get; } = commas;
    public List<Expression> Expressions { get; } = expressions;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTupleExpression(this);
}
