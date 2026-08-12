using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class PropertyDeclaration(Token? mutKeyword, Token name, ColonTypeClause colonTypeClause, Attributes? attributes)
    : NamedDeclaration([mutKeyword], name, colonTypeClause, attributes),
      IWithAttributes
{
    public Token? MutKeyword { get; } = mutKeyword;
    public ColonTypeClause ColonTypeClause { get; } = colonTypeClause;
    public Attributes? Attributes { get; } = attributes;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitPropertyDeclaration(this);
}