using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class ExportSpecifier(Token name, Token? asKeyword, Token? alias)
    : Statement([name, asKeyword, alias], [])
{
    public Token Name { get; } = name;

    public Token? AsKeyword { get; } = asKeyword;
    public Token? Alias { get; } = alias;

    public Token ExportedName => Alias ?? Name;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitExportSpecifier(this);
}