using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypeArguments(Token leftArrow, Token rightArrow, List<TypeExpression> argumentsList)
    : TypeArguments<TypeExpression>(leftArrow, rightArrow, argumentsList);

public class TypeArguments<TType>(Token leftArrow, Token rightArrow, List<TType> argumentsList)
    : Node([leftArrow, rightArrow], argumentsList)
    where TType : TypeExpression
{
    public Token LeftArrow { get; } = leftArrow;
    public Token RightArrow { get; } = rightArrow;
    public List<TType> ArgumentsList { get; } = argumentsList;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeArguments(this);
}