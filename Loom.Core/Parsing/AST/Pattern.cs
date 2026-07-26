using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public abstract class Pattern(IEnumerable<Token?> theseTokens, IEnumerable<Node?> children)
    : Node(theseTokens, children);