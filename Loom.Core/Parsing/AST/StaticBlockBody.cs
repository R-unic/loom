using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class StaticBlockBody(Token leftBrace, Token rightBrace, List<StaticFieldDeclaration> fields, List<FunctionDeclaration> methods)
    : Statement([leftBrace, rightBrace], [..fields, ..methods])
{
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<StaticFieldDeclaration> Fields { get; } = fields;
    public List<FunctionDeclaration> Methods { get; } = methods;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitStaticBlockBody(this);
}
