using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class EventDeclaration(Token keyword, Token name, TypeParameters? typeParameters, Parameters? parameters, Attributes? attributes)
    : GenericNamedDeclaration([], keyword, name, typeParameters, attributes),
      IWithAttributes
{
    public Parameters? Parameters { get; } = parameters;
    public Attributes? Attributes { get; } = attributes;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEventDeclaration(this);
}