using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ElementAccess(Token? questionMark, Token leftBracket, Token rightBracket, Expression expression, Expression indexExpression)
    : AssignmentTarget([questionMark, leftBracket, rightBracket], [expression, indexExpression])
{
    public Token? QuestionMark { get; } = questionMark;
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public Expression Expression { get; } = expression;
    public Expression IndexExpression { get; } = indexExpression;
    public bool IsOptional => QuestionMark != null;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitElementAccess(this);
}