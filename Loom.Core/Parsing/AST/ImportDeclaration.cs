using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ImportDeclaration(
    Token importKeyword,
    Token? typeKeyword,
    Token leftBrace,
    List<ImportSpecifier> specifiers,
    Token rightBrace,
    Token fromKeyword,
    Literal moduleSpecifier
)
    : Statement(
        [
            importKeyword,
            typeKeyword,
            leftBrace,
            rightBrace,
            fromKeyword,
            ..specifiers.SelectMany(s => s.Tokens),
            ..moduleSpecifier.Tokens
        ],
        [..specifiers, moduleSpecifier]
    )
{
    public Token ImportKeyword { get; } = importKeyword;
    public Token? TypeKeyword { get; } = typeKeyword;
    public Token LeftBrace { get; } = leftBrace;
    public List<ImportSpecifier> Specifiers { get; } = specifiers;
    public Token RightBrace { get; } = rightBrace;
    public Token FromKeyword { get; } = fromKeyword;
    public Literal ModuleSpecifier { get; } = moduleSpecifier;

    public bool IsTypeOnly => TypeKeyword != null;
    public string? ModulePath => ModuleSpecifier.Value as string;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitImportDeclaration(this);
}