using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class StaticBlock(Token keyword, TypeName interfaceName, StaticBlockBody body)
    : Statement([keyword, interfaceName.Name], [interfaceName, body])
{
    public Token Keyword { get; } = keyword;
    public TypeName InterfaceName { get; } = interfaceName;
    public StaticBlockBody Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitStaticBlock(this);
}
