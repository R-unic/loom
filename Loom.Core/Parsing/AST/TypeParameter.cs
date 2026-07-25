using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class TypeParameter(Token? varianceKeyword, Token name, ColonTypeClause? colonTypeClause, EqualsTypeClause? equalsTypeClause)
    : NamedDeclaration([varianceKeyword, ..colonTypeClause?.Tokens ?? [], ..equalsTypeClause?.Tokens ?? []], name, colonTypeClause, equalsTypeClause)
{
    public Token? VarianceKeyword { get; } = varianceKeyword;
    public ColonTypeClause? ColonTypeClause { get; } = colonTypeClause;
    public EqualsTypeClause? EqualsTypeClause { get; } = equalsTypeClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeParameter(this);
}