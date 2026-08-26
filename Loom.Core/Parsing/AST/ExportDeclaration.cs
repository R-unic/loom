using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ExportDeclaration(Token exportKeyword, NamedDeclaration declaration)
    : Statement([exportKeyword], [declaration])
{
    public Token ExportKeyword { get; } = exportKeyword;
    public NamedDeclaration Declaration { get; } = declaration;

    /// <summary>Written with <c>internal</c> rather than <c>export</c> - see <see cref="Resolving.ExportBinding.IsInternal" />.</summary>
    public bool IsInternal => ExportKeyword.Kind == SyntaxKind.InternalKeyword;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportDeclaration(this);
}