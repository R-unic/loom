using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ExportDeclaration(Token exportKeyword, NamedDeclaration declaration)
    : Statement([exportKeyword], [declaration])
{
    public Token ExportKeyword { get; } = exportKeyword;
    public NamedDeclaration Declaration { get; } = declaration;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportDeclaration(this);
}