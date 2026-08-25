using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class StaticFieldDeclaration(Token name, ColonTypeClause? colonTypeClause, EqualsValueClause equalsValueClause)
    : NamedDeclaration([], name, colonTypeClause, equalsValueClause)
{
    public ColonTypeClause? ColonTypeClause { get; } = colonTypeClause;
    public EqualsValueClause EqualsValueClause { get; } = equalsValueClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitStaticFieldDeclaration(this);
}
