using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ObjectDestructuringTarget(Token leftBrace, Token rightBrace, List<ObjectDestructuringField> fields)
    : DestructuringTarget([leftBrace, rightBrace], fields)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<ObjectDestructuringField> Fields { get; } = fields;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitObjectDestructuringTarget(this);
}
