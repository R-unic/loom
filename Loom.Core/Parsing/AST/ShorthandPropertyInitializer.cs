namespace Loom.Core.Parsing.AST;

public class ShorthandPropertyInitializer(Identifier identifier)
    : InterfaceInvocationInitializer(identifier, [])
{
    public Identifier Identifier { get; } = identifier;
    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceInvocationShorthandPropertyInitializer(this);
}