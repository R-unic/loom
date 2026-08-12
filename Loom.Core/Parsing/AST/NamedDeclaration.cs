using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public abstract class NamedDeclaration(IEnumerable<Token?> otherTokens, Token name, params Node?[] children)
    : Statement([name, ..otherTokens], children)
{
    public Token Name { get; } = name;
}