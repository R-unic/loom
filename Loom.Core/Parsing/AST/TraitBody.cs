using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class TraitBody(Token leftBrace, Token rightBrace, List<DeclareFunctionSignature> members)
    : Statement([leftBrace, rightBrace, ..members.SelectMany(m => m.Tokens)], members)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<DeclareFunctionSignature> Members { get; } = members;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTraitBody(this);
}