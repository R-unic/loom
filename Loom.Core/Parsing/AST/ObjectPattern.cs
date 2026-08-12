using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class ObjectPattern(Token leftBrace, Token rightBrace, List<ObjectPatternField> fields)
    : Pattern([leftBrace, rightBrace], fields)
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<ObjectPatternField> Fields { get; } = fields;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitObjectPattern(this);
}