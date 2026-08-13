using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>let R</c> inside a type pattern - the name whatever stood in that position is bound to.
/// </summary>
/// <remarks>
///     Loom already binds names inside patterns with <c>let</c> at the value level, so a type pattern
///     uses the same word rather than a second binder keyword that exists only in types. A constraint
///     is written the way a type parameter's is - <c>let K: number</c>.
/// </remarks>
public class InferType(Token keyword, Token name, ColonTypeClause? colonTypeClause)
    : TypeExpression([keyword, name], [colonTypeClause])
{
    public Token Keyword { get; } = keyword;
    public Token Name { get; } = name;
    public ColonTypeClause? ColonTypeClause { get; } = colonTypeClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInferType(this);
}
