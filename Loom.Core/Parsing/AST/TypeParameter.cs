using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypeParameter(Token name, ColonTypeClause? colonTypeClause, EqualsTypeClause? equalsTypeClause)
    : NamedDeclaration([..colonTypeClause?.Tokens ?? [], ..equalsTypeClause?.Tokens ?? []], name, colonTypeClause, equalsTypeClause)
{
    public ColonTypeClause? ColonTypeClause { get; } = colonTypeClause;
    public EqualsTypeClause? EqualsTypeClause { get; } = equalsTypeClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeParameter(this);
}