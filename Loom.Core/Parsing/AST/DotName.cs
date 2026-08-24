using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class DotName(Token dot, Token name)
{
    public Token Dot { get; } = dot;
    public Token Name { get; } = name;
    public List<Token> Tokens { get; } = [dot, name];
    public bool IsOptional => Dot.Kind == SyntaxKind.QuestionDot;
    public bool IsStatic => Dot.Kind == SyntaxKind.ColonColon;
}