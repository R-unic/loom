using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ExportDeclaration(Token exportKeyword, Statement declaration)
    : Statement([exportKeyword], [declaration])
{
    public Token ExportKeyword { get; } = exportKeyword;

    /// <summary>
    ///     The exported declaration - a <see cref="NamedDeclaration" /> for everything exportable in its own
    ///     right, or a <see cref="Declare" /> for <c>export declare ...</c>, which wraps one instead of being
    ///     one (its ambient signature is what carries the name).
    /// </summary>
    public Statement Declaration { get; } = declaration;

    /// <summary>Written with <c>internal</c> rather than <c>export</c> - see <see cref="Resolving.ExportBinding.IsInternal" />.</summary>
    public bool IsInternal => ExportKeyword.Kind == SyntaxKind.InternalKeyword;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportDeclaration(this);
}