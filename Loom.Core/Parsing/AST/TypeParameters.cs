using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypeParameters(Token leftArrow, Token rightArrow, List<TypeParameter> parameterList)
    : Node([leftArrow, ..parameterList.SelectMany(p => p.Tokens), rightArrow], parameterList)
{
    public Token LeftArrow { get; } = leftArrow;
    public Token RightArrow { get; } = rightArrow;
    public List<TypeParameter> ParameterList { get; } = parameterList;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeParameters(this);
}