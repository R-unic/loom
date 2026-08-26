using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ObjectDestructuringField(Token name, Token? colon, Token? alias, DestructuringTarget? nestedTarget = null)
    : Node([name, colon, alias], [nestedTarget])
{
    public Token Name { get; } = name;
    public Token? Colon { get; } = colon;
    public Token? Alias { get; } = alias;

    /// <summary>
    ///     The pattern <c>address</c> renames into, when it renames into a pattern instead of a plain name -
    ///     <c>{ address: { city } }</c>. Mutually exclusive with <see cref="Alias" />: a field either binds a
    ///     single name (itself or its alias) or destructures further, never both.
    /// </summary>
    public DestructuringTarget? NestedTarget { get; } = nestedTarget;

    /// <summary>The name this field binds at this level. Only meaningful when <see cref="NestedTarget" /> is null.</summary>
    public Token BindingName => Alias ?? Name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitObjectDestructuringField(this);
}
