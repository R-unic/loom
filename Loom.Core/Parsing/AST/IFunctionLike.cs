using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public interface IFunctionLike
{
    public Parameters? Parameters { get; }
    public ColonTypeClause? ReturnType { get; }
    public Statement Body { get; }

    /// <summary>The <c>async</c> written before <c>fn</c>, or null when the function is a synchronous one.</summary>
    public Token? AsyncKeyword { get; }
}
