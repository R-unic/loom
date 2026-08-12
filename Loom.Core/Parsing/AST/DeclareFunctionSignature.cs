using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class DeclareFunctionSignature(
    Token keyword,
    Token name,
    TypeParameters? typeParameters,
    Parameters? parameters,
    ColonTypeClause returnType,
    Attributes? attributes = null,
    Token? asyncKeyword = null,
    params Node?[] extraChildren
)
    : GenericNamedDeclaration(asyncKeyword == null ? [] : [asyncKeyword], keyword, name, typeParameters, [parameters, returnType, attributes, ..extraChildren]),
      IWithAttributes
{
    public Parameters? Parameters { get; } = parameters;
    public ColonTypeClause ReturnType { get; } = returnType;
    public Attributes? Attributes { get; } = attributes;
    public Token? AsyncKeyword { get; } = asyncKeyword;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitDeclareFunctionSignature(this);
}
