using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class FunctionType(Token fnKeyword, TypeParameters? typeParameters, Parameters? parameters, ColonTypeClause returnType, Token? asyncKeyword = null)
    : TypeExpression(
        [fnKeyword, asyncKeyword, ..typeParameters?.Tokens ?? [], ..parameters?.Tokens ?? [], ..returnType.Tokens],
        [typeParameters, parameters, returnType]
    )
{
    public Token FnKeyword { get; } = fnKeyword;
    public TypeParameters? TypeParameters { get; } = typeParameters;
    public Parameters? Parameters { get; } = parameters;
    public ColonTypeClause ReturnType { get; } = returnType;
    public Token? AsyncKeyword { get; } = asyncKeyword;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitFunctionType(this);
}
