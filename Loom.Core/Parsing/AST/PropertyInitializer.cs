using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class PropertyInitializer(Token name, Token colon, Expression expression)
    : InterfaceInvocationInitializer(expression, [name, colon])
{
    public Token Name { get; } = name;
    public Token Colon { get; } = colon;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceInvocationPropertyInitializer(this);
}