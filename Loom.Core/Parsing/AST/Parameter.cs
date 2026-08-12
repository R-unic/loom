using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Parameter(Token? dotDot, Token name, ColonTypeClause? colonTypeClause, EqualsValueClause? equalsValueClause)
    : NamedDeclaration([dotDot], name, colonTypeClause, equalsValueClause)
{
    public Token? DotDot { get; } = dotDot;
    public ColonTypeClause? ColonTypeClause { get; } = colonTypeClause;
    public EqualsValueClause? EqualsValueClause { get; } = equalsValueClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitParameter(this);
}