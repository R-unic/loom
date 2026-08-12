using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ColonTypeClause(Token colonToken, TypeExpression type)
    : TypeExpression([colonToken], [type])
{
    public Token ColonToken { get; } = colonToken;
    public TypeExpression Type { get; } = type;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitColonTypeClause(this);
}