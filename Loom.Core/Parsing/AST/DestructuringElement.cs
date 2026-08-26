using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class DestructuringElement(Token? name, DestructuringTarget? nestedTarget = null)
    : Node([name], [nestedTarget])
{
    /// <summary>Null when this element destructures further instead of binding a single name - see <see cref="NestedTarget" />.</summary>
    public Token? Name { get; } = name;

    /// <summary>The pattern this position destructures into, when it isn't a plain name - <c>[{ x }]</c> or <c>[[a, b]]</c>.</summary>
    public DestructuringTarget? NestedTarget { get; } = nestedTarget;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitDestructuringElement(this);
}
