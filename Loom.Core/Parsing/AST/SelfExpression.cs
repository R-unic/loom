using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class SelfExpression(Token atToken) : Expression([atToken], [])
{
    public Token AtToken { get; } = atToken;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitSelfExpression(this);

    /// <summary>
    ///     Whether this '@' sits inside a trait's own default method body - directly, or nested inside a
    ///     local function/closure written within that body. Which enclosing function is nearest doesn't
    ///     matter, unlike <see cref="FirstAncestorOfType{T}" /> alone would answer: a closure written
    ///     inside a default body doesn't rebind what '@' refers to, so any 'FunctionDeclaration' ancestor
    ///     parented by a 'TraitBody' - not only the nearest one - means yes. Only meaningful once the
    ///     caller has already ruled out an enclosing <see cref="Implement" />, which binds '@' to something
    ///     else entirely.
    /// </summary>
    public bool IsInsideDefaultMethodBody()
    {
        for (var current = Parent; current != null; current = current.Parent)
            if (current is FunctionDeclaration { Parent: TraitBody })
                return true;

        return false;
    }
}
