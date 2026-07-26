using System.Diagnostics.CodeAnalysis;
using Loom.Core.Resolving;
using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class EventDeclaration(Token keyword, Token name, TypeParameters? typeParameters, Parameters? parameters, Attributes? attributes)
    : GenericNamedDeclaration([], keyword, name, typeParameters, attributes),
      IWithAttributes
{
    public Parameters? Parameters { get; } = parameters;
    public Attributes? Attributes { get; } = attributes;

    public bool TryGetIntrinsicAttribute(SemanticModel semanticModel, string name, [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        var eventSymbol = semanticModel.GetDeclarationSymbols(this).OfType<PropertySymbol>().FirstOrDefault();
        attribute = eventSymbol?.Attributes.Find(a => a is { IsIntrinsic: true } && a.Name == name);
        return attribute != null;
    }

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEventDeclaration(this);
}