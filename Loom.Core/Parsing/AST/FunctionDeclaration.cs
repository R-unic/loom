using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class FunctionDeclaration(
    Token keyword,
    Token name,
    TypeParameters? typeParameters,
    Parameters? parameters,
    ColonTypeClause? returnType,
    Statement body,
    Attributes? attributes = null,
    Token? asyncKeyword = null
)
    : DeclareFunctionSignature(
        keyword,
        name,
        typeParameters,
        parameters,
        returnType!,
        attributes,
        asyncKeyword,
        body
    ),
      IFunctionLike
{
    public new ColonTypeClause? ReturnType { get; } = returnType;
    public Statement Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitFunctionDeclaration(this);
}
