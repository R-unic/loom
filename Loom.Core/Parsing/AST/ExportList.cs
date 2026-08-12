using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ExportList(
    Token exportKeyword,
    Token? typeKeyword,
    Token leftBrace,
    List<ExportSpecifier> specifiers,
    Token rightBrace,
    Token? fromKeyword,
    Literal? moduleSpecifier
)
    : Statement(
        [
            exportKeyword,
            typeKeyword,
            leftBrace,
            rightBrace,
            fromKeyword
        ],
        [..specifiers, moduleSpecifier]
    ), IReExport
{
    public Token ExportKeyword { get; } = exportKeyword;
    public Token? TypeKeyword { get; } = typeKeyword;
    public Token LeftBrace { get; } = leftBrace;
    public List<ExportSpecifier> Specifiers { get; } = specifiers;
    public Token RightBrace { get; } = rightBrace;
    public Token? FromKeyword { get; } = fromKeyword;
    public Literal? ModuleSpecifier { get; } = moduleSpecifier;

    public bool IsTypeOnly => TypeKeyword != null;
    public bool IsReExport => ModuleSpecifier != null;
    public string? ModulePath => ModuleSpecifier?.Value as string;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportList(this);
}