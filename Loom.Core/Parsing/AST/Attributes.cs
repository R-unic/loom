using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Attributes(Token leftBracket, Token rightBracket, List<Attribute> attributeList)
    : Statement([leftBracket, rightBracket, ..attributeList.SelectMany(a => a.Tokens)], attributeList)
{
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public List<Attribute> AttributeList { get; } = attributeList;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitAttributes(this);
}