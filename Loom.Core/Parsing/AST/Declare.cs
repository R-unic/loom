using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Declare(Token keyword, DeclareSignature signature)
    : Statement([keyword, ..signature.Tokens], [signature])
{
    public Token Keyword { get; } = keyword;
    public DeclareSignature Signature { get; } = signature;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitDeclare(this);
}