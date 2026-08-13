using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>_</c> inside a type pattern - a position the arm matches but does not care about.
/// </summary>
public class WildcardType(Token token) : TypeExpression([token], [])
{
    public Token Token { get; } = token;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitWildcardType(this);
}
