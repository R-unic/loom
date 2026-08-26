using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>export * from "./m"</c>: forwards every name another module exports, under the names it exports
///     them with. <c>export type * from "./m"</c> forwards only its types.
/// </summary>
public sealed class ExportAll(
    Token exportKeyword,
    Token? typeKeyword,
    Token star,
    Token fromKeyword,
    Literal moduleSpecifier
)
    : Statement([exportKeyword, typeKeyword, star, fromKeyword], [moduleSpecifier]), IReExport
{
    public Token ExportKeyword { get; } = exportKeyword;
    public Token? TypeKeyword { get; } = typeKeyword;
    public Token Star { get; } = star;
    public Token FromKeyword { get; } = fromKeyword;
    public Literal? ModuleSpecifier { get; } = moduleSpecifier;

    public bool IsTypeOnly => TypeKeyword != null;
    public bool IsReExport => true;
    public bool IsInternal => ExportKeyword.Kind == SyntaxKind.InternalKeyword;
    public string? ModulePath => ModuleSpecifier?.Value as string;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportAll(this);
}
