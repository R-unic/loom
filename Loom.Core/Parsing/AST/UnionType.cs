using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class UnionType(List<Token> pipes, List<TypeExpression> types)
    : TypeExpression([..pipes], types)
{
    public List<Token> Pipes { get; } = pipes;
    public List<TypeExpression> Types { get; } = types;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitUnionType(this);
}