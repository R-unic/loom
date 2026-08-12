using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TupleType(Token leftParen, Token rightParen, List<Token> commas, List<TypeExpression> types)
    : TypeExpression([leftParen, rightParen, ..commas], types)
{
    public Token LeftParen { get; } = leftParen;
    public Token RightParen { get; } = rightParen;
    public List<Token> Commas { get; } = commas;
    public List<TypeExpression> Types { get; } = types;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTupleType(this);
}
