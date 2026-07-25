using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public sealed class InterpolationTextPart(Token token) : InterpolationPart
{
    public Token Token { get; } = token;
    public string Text => Token.Text;
}
