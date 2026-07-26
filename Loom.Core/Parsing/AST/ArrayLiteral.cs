using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ArrayLiteral(Token? mutKeyword, Token leftBracket, Token rightBracket, List<Expression> expressions)
    : Expression([mutKeyword, leftBracket, ..expressions.SelectMany(p => p.Tokens), rightBracket], expressions)
{
    public Token? MutKeyword { get; } = mutKeyword;
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public List<Expression> Expressions { get; } = expressions;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitArrayLiteral(this);
}