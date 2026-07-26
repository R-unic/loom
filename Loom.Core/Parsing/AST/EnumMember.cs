using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class EnumMember(Token name, EqualsValueClause? equalsValueClause)
    : Statement([name, ..equalsValueClause?.Tokens ?? []], [equalsValueClause])
{
    public Token Name { get; } = name;
    public EqualsValueClause? EqualsValueClause { get; } = equalsValueClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitEnumMember(this);
}