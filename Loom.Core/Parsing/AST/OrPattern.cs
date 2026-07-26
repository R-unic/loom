using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class OrPattern(List<Token> pipes, List<Pattern> patterns)
    : Pattern([..pipes, ..patterns.SelectMany(p => p.Tokens)], patterns)
{
    public List<Token> Pipes { get; } = pipes;
    public List<Pattern> Patterns { get; } = patterns;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitOrPattern(this);
}