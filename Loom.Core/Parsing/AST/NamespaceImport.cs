using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class NamespaceImport(
    Token importKeyword,
    Token star,
    Token asKeyword,
    Token name,
    Token fromKeyword,
    Literal moduleSpecifier
)
    : Statement([importKeyword, star, asKeyword, name, fromKeyword], [moduleSpecifier])
{
    public Token ImportKeyword { get; } = importKeyword;
    public Token Star { get; } = star;
    public Token AsKeyword { get; } = asKeyword;
    public Token Name { get; } = name;
    public Token FromKeyword { get; } = fromKeyword;
    public Literal ModuleSpecifier { get; } = moduleSpecifier;

    public string? ModulePath => ModuleSpecifier.Value as string;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitNamespaceImport(this);
}