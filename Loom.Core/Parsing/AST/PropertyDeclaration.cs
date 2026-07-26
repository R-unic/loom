using System.Diagnostics.CodeAnalysis;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class PropertyDeclaration(Token? mutKeyword, Token name, ColonTypeClause colonTypeClause, Attributes? attributes)
    : NamedDeclaration([mutKeyword, ..colonTypeClause.Tokens, ..attributes?.Tokens ?? []], name, colonTypeClause, attributes),
      IWithAttributes
{
    public Token? MutKeyword { get; } = mutKeyword;
    public ColonTypeClause ColonTypeClause { get; } = colonTypeClause;
    public Attributes? Attributes { get; } = attributes;

    public bool TryGetIntrinsicAttribute(SemanticModel semanticModel, string name, [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        var propertySymbol = semanticModel.GetDeclarationSymbol(this, SymbolKind.Property) as PropertySymbol;
        attribute = propertySymbol?.Attributes.Find(a => a is { IsIntrinsic: true } && a.Name == name);
        return attribute != null;
    }

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitPropertyDeclaration(this);
}