using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ColonTypeListClause(Token colonToken, List<TypeExpression> types)
    : TypeExpression([colonToken], types)
{
    public Token ColonToken { get; } = colonToken;
    public List<TypeExpression> Types { get; } = types;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitColonTypeListClause(this);
}