using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public abstract class InterfaceInvocationInitializer(Expression expression, IEnumerable<Token?> otherTokens, IEnumerable<Node?>? extraChildren = null)
    : Expression([..otherTokens], [..extraChildren ?? [], expression])
{
    public Expression Expression { get; } = expression;
}