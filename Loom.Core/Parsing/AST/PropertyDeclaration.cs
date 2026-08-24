using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class PropertyDeclaration(Token? staticKeyword, Token? mutKeyword, Token name, ColonTypeClause colonTypeClause, Attributes? attributes)
    : NamedDeclaration([mutKeyword, staticKeyword], name, colonTypeClause, attributes),
      IWithAttributes
{
    public Token? StaticKeyword { get; } = staticKeyword;
    public Token? MutKeyword { get; } = mutKeyword;
    public ColonTypeClause ColonTypeClause { get; } = colonTypeClause;
    public Attributes? Attributes { get; } = attributes;
    public bool IsStatic => StaticKeyword != null;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitPropertyDeclaration(this);
}