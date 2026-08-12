using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public interface IFunctionLike
{
    Parameters? Parameters { get; }
    ColonTypeClause? ReturnType { get; }
    Statement Body { get; }

    /// <summary>The <c>async</c> written before <c>fn</c>, or null when the function is a synchronous one.</summary>
    Token? AsyncKeyword { get; }
}
