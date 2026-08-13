using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>One <c>Pattern -&gt; Type</c> of a <see cref="TypeMatch" />.</summary>
public class TypeMatchArm(TypeExpression pattern, Token arrow, TypeExpression result)
    : Node([arrow], [pattern, result])
{
    /// <summary>The type the subject is measured against. May contain <see cref="InferType" /> binders and <see cref="WildcardType" />s.</summary>
    public TypeExpression Pattern { get; } = pattern;
    public Token Arrow { get; } = arrow;
    public TypeExpression Result { get; } = result;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeMatchArm(this);
}
