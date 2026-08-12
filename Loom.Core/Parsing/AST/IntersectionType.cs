using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class IntersectionType(List<Token> ampersands, List<TypeExpression> types)
    : TypeExpression([..ampersands], types)
{
    public List<Token> Ampersands { get; } = ampersands;
    public List<TypeExpression> Types { get; } = types;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIntersectionType(this);
}